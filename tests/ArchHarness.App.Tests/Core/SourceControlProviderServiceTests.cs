using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ArchHarness.App.SourceControl;
using ArchHarness.App.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArchHarness.App.Tests.Core;

public sealed class SourceControlProviderServiceTests
{
    private static GitHubSourceControlService CreateGitHubService(HttpMessageHandler handler)
        => new GitHubSourceControlService(new HttpClient(handler), NullLogger<GitHubSourceControlService>.Instance);

    /// <summary>
    /// AzureDevOpsServer — GetPullRequestsAsync — ParsesSummaries
    /// </summary>
    [Fact]
    public async Task AzureDevOpsServer_GetPullRequestsAsync_ParsesSummaries()
    {
        string? requestUri = null;
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            requestUri = request.RequestUri?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "value": [
                        {
                          "pullRequestId": 42,
                          "title": "Add workspace source control",
                          "createdBy": {
                            "displayName": "Dana"
                          },
                          "sourceRefName": "refs/heads/feature/source-control",
                          "targetRefName": "refs/heads/main",
                          "status": "active",
                          "creationDate": "2026-03-17T12:00:00Z",
                          "_links": {
                            "web": {
                              "href": "https://ado.contoso.local/pr/42"
                            }
                          }
                        }
                      ]
                    }
                    """, Encoding.UTF8, "application/json")
            };
        });

        AzureDevOpsSourceControlService service = new AzureDevOpsSourceControlService(new HttpClient(handler));
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServer,
            DisplayName = "Contoso Server",
            ServerUrl = "https://ado.contoso.local/tfs",
            Organization = "DefaultCollection",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        };

        IReadOnlyList<PullRequestSummary> pullRequests = await service.GetPullRequestsAsync(settings, "Harness Project", "ArchHarness Repo", CancellationToken.None);

        PullRequestSummary pullRequest = Assert.Single(pullRequests);
        Assert.Equal("https://ado.contoso.local/tfs/DefaultCollection/Harness Project/_apis/git/repositories/ArchHarness Repo/pullrequests?api-version=7.0&searchCriteria.status=active", requestUri);
        Assert.Equal("42", pullRequest.Id);
        Assert.Equal("Dana", pullRequest.Author);
        Assert.Equal("feature/source-control", pullRequest.SourceBranch);
        Assert.Equal("main", pullRequest.TargetBranch);
        Assert.Equal("Harness Project", pullRequest.ProjectName);
        Assert.Equal("ArchHarness Repo", pullRequest.RepositoryName);
        Assert.Equal("https://ado.contoso.local/pr/42", pullRequest.Url);
        Assert.Equal(DateTimeOffset.Parse("2026-03-17T12:00:00Z", CultureInfo.InvariantCulture), pullRequest.CreatedDate);
    }

    /// <summary>
    /// GitHub — GetPullRequestsAsync — UsesUserAgentAndBearerToken
    /// </summary>
    [Fact]
    public async Task GitHub_GetPullRequestsAsync_UsesUserAgentAndBearerToken()
    {
        string? requestUri = null;
        AuthenticationHeaderValue? authorization = null;
        string? userAgent = null;
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            requestUri = request.RequestUri?.ToString();
            authorization = request.Headers.Authorization;
            userAgent = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [
                      {
                        "number": 7,
                        "title": "Add source control providers",
                        "user": {
                          "login": "octocat"
                        },
                        "head": {
                          "ref": "feature/source-control"
                        },
                        "base": {
                          "ref": "main"
                        },
                        "state": "open",
                        "draft": false,
                        "html_url": "https://github.com/octo-org/archharness/pull/7",
                        "created_at": "2026-03-17T13:30:00Z"
                      }
                    ]
                    """, Encoding.UTF8, "application/json")
            };
        });

        GitHubSourceControlService service = CreateGitHubService(handler);
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        };

        IReadOnlyList<PullRequestSummary> pullRequests = await service.GetPullRequestsAsync(settings, null, "archharness", CancellationToken.None);

        PullRequestSummary pullRequest = Assert.Single(pullRequests);
        Assert.Equal("https://api.github.com/repos/octo-org/archharness/pulls?state=open&per_page=100&page=1", requestUri);
        Assert.NotNull(authorization);
        Assert.Equal("Bearer", authorization!.Scheme);
        Assert.Equal("github-pat", authorization.Parameter);
        Assert.Contains("ArchHarness", userAgent);
        Assert.Equal("7", pullRequest.Id);
        Assert.Equal("octocat", pullRequest.Author);
        Assert.Equal("open", pullRequest.Status);
        Assert.Equal("octo-org", pullRequest.ProjectName);
        Assert.Equal("archharness", pullRequest.RepositoryName);
    }

    /// <summary>
    /// AzureDevOpsServer — GetPullRequestFilesAsync — ParsesChangedFiles
    /// </summary>
    [Fact]
    public async Task AzureDevOpsServer_GetPullRequestFilesAsync_ParsesChangedFiles()
    {
        Queue<HttpResponseMessage> responses = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "value": [
                        { "id": 1 },
                        { "id": 3 }
                      ]
                    }
                    """, Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "changeEntries": [
                        {
                          "changeType": "add",
                          "item": {
                            "path": "/src/ArchHarness.App/SourceControl/PullRequestFile.cs"
                          }
                        },
                        {
                          "changeType": "rename",
                          "item": {
                            "path": "/src/ArchHarness.App/SourceControl/PullRequestSummary.cs"
                          }
                        }
                      ]
                    }
                    """, Encoding.UTF8, "application/json")
            }
        });
        List<string> requestUris = new List<string>();
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            requestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
            return responses.Dequeue();
        });

        AzureDevOpsSourceControlService service = new AzureDevOpsSourceControlService(new HttpClient(handler));
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServer,
            DisplayName = "Contoso Server",
            ServerUrl = "https://ado.contoso.local/tfs",
            Organization = "DefaultCollection",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        };

        IReadOnlyList<PullRequestFile> files = await service.GetPullRequestFilesAsync(
            settings,
            "Harness Project",
            "ArchHarness Repo",
            "42",
            CancellationToken.None);

        Assert.Equal("https://ado.contoso.local/tfs/DefaultCollection/Harness Project/_apis/git/repositories/ArchHarness Repo/pullRequests/42/iterations?api-version=7.0", requestUris[0]);
        Assert.Equal("https://ado.contoso.local/tfs/DefaultCollection/Harness Project/_apis/git/repositories/ArchHarness Repo/pullRequests/42/iterations/3/changes?api-version=7.0", requestUris[1]);
        Assert.Collection(
            files,
            file =>
            {
                Assert.Equal("/src/ArchHarness.App/SourceControl/PullRequestFile.cs", file.Path);
                Assert.Equal(PullRequestFileChangeTypes.ADDED, file.ChangeType);
            },
            file =>
            {
                Assert.Equal("/src/ArchHarness.App/SourceControl/PullRequestSummary.cs", file.Path);
                Assert.Equal(PullRequestFileChangeTypes.RENAMED, file.ChangeType);
            });
    }

    /// <summary>
    /// AzureDevOpsServer — GetPullRequestsAsync — SkipsProjectsThatRejectEnumeration
    /// </summary>
    [Fact]
    public async Task AzureDevOpsServer_GetPullRequestsAsync_SkipsProjectsThatRejectEnumeration()
    {
        List<string> requestUris = new List<string>();
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            string requestUri = request.RequestUri?.ToString() ?? string.Empty;
            requestUris.Add(requestUri);

            return requestUri switch
            {
                "https://ado.contoso.local/tfs/DefaultCollection/_apis/projects?api-version=6.0" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "value": [
                            { "name": "Restricted Project" },
                            { "name": "Accessible Project" }
                          ]
                        }
                        """, Encoding.UTF8, "application/json")
                },
                "https://ado.contoso.local/tfs/DefaultCollection/Restricted Project/_apis/git/repositories?api-version=7.0" => new HttpResponseMessage(HttpStatusCode.Forbidden),
                "https://ado.contoso.local/tfs/DefaultCollection/Accessible Project/_apis/git/repositories?api-version=7.0" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "value": [
                            { "name": "ArchHarness Repo" }
                          ]
                        }
                        """, Encoding.UTF8, "application/json")
                },
                "https://ado.contoso.local/tfs/DefaultCollection/Accessible Project/_apis/git/repositories/ArchHarness Repo/pullrequests?api-version=7.0&searchCriteria.status=active" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "value": [
                            {
                              "pullRequestId": 101,
                              "title": "Visible PR",
                              "createdBy": {
                                "displayName": "Dana"
                              },
                              "sourceRefName": "refs/heads/feature/visible-pr",
                              "targetRefName": "refs/heads/main",
                              "status": "active",
                              "creationDate": "2026-03-18T12:00:00Z",
                              "_links": {
                                "web": {
                                  "href": "https://ado.contoso.local/pr/101"
                                }
                              }
                            }
                          ]
                        }
                        """, Encoding.UTF8, "application/json")
                },
                _ => throw new Xunit.Sdk.XunitException($"Unexpected Azure DevOps request URI: {requestUri}")
            };
        });

        AzureDevOpsSourceControlService service = new AzureDevOpsSourceControlService(new HttpClient(handler));
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServer,
            DisplayName = "Contoso Server",
            ServerUrl = "https://ado.contoso.local/tfs",
            Organization = "DefaultCollection",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        };

        IReadOnlyList<PullRequestSummary> pullRequests = await service.GetPullRequestsAsync(settings, null, null, CancellationToken.None);

        PullRequestSummary pullRequest = Assert.Single(pullRequests);
        Assert.Equal("101", pullRequest.Id);
        Assert.Equal("Accessible Project", pullRequest.ProjectName);
        Assert.Equal("ArchHarness Repo", pullRequest.RepositoryName);
        Assert.Contains("https://ado.contoso.local/tfs/DefaultCollection/Restricted Project/_apis/git/repositories?api-version=7.0", requestUris);
        Assert.Contains("https://ado.contoso.local/tfs/DefaultCollection/Accessible Project/_apis/git/repositories?api-version=7.0", requestUris);
    }

    /// <summary>
    /// AzureDevOpsServer — GetPullRequestsAsync — UsesProjectFilterWithoutListingAllProjects
    /// </summary>
    [Fact]
    public async Task AzureDevOpsServer_GetPullRequestsAsync_UsesProjectFilterWithoutListingAllProjects()
    {
        List<string> requestUris = new List<string>();
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            string requestUri = request.RequestUri?.ToString() ?? string.Empty;
            requestUris.Add(requestUri);

            return requestUri switch
            {
                "https://ado.contoso.local/tfs/DefaultCollection/Harness Project/_apis/git/repositories?api-version=7.0" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "value": [
                            { "name": "ArchHarness Repo" }
                          ]
                        }
                        """, Encoding.UTF8, "application/json")
                },
                "https://ado.contoso.local/tfs/DefaultCollection/Harness Project/_apis/git/repositories/ArchHarness Repo/pullrequests?api-version=7.0&searchCriteria.status=active" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "value": [
                            {
                              "pullRequestId": 42,
                              "title": "Targeted PR",
                              "createdBy": {
                                "displayName": "Dana"
                              },
                              "sourceRefName": "refs/heads/feature/targeted-pr",
                              "targetRefName": "refs/heads/main",
                              "status": "active",
                              "creationDate": "2026-03-18T12:00:00Z",
                              "_links": {
                                "web": {
                                  "href": "https://ado.contoso.local/pr/42"
                                }
                              }
                            }
                          ]
                        }
                        """, Encoding.UTF8, "application/json")
                },
                "https://ado.contoso.local/tfs/DefaultCollection/_apis/projects?api-version=6.0" => throw new Xunit.Sdk.XunitException("Projects endpoint should not be called when projectFilter is supplied."),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected Azure DevOps request URI: {requestUri}")
            };
        });

        AzureDevOpsSourceControlService service = new AzureDevOpsSourceControlService(new HttpClient(handler));
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServer,
            DisplayName = "Contoso Server",
            ServerUrl = "https://ado.contoso.local/tfs",
            Organization = "DefaultCollection",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        };

        IReadOnlyList<PullRequestSummary> pullRequests = await service.GetPullRequestsAsync(
            settings,
            null,
            null,
            CancellationToken.None,
            projectFilter: "Harness Project");

        PullRequestSummary pullRequest = Assert.Single(pullRequests);
        Assert.Equal("42", pullRequest.Id);
        Assert.Equal("Harness Project", pullRequest.ProjectName);
        Assert.DoesNotContain("https://ado.contoso.local/tfs/DefaultCollection/_apis/projects?api-version=6.0", requestUris);
    }

    /// <summary>
    /// AzureDevOpsServer — StreamPullRequestBatchesAsync — YieldsRepositoryBatches
    /// </summary>
    [Fact]
    public async Task AzureDevOpsServer_StreamPullRequestBatchesAsync_YieldsRepositoryBatches()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            string requestUri = request.RequestUri?.ToString() ?? string.Empty;
            return requestUri switch
            {
                "https://ado.contoso.local/tfs/DefaultCollection/_apis/projects?api-version=6.0" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "value": [
                            { "name": "Project A" }
                          ]
                        }
                        """, Encoding.UTF8, "application/json")
                },
                "https://ado.contoso.local/tfs/DefaultCollection/Project A/_apis/git/repositories?api-version=7.0" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "value": [
                            { "name": "Repo One" },
                            { "name": "Repo Two" }
                          ]
                        }
                        """, Encoding.UTF8, "application/json")
                },
                "https://ado.contoso.local/tfs/DefaultCollection/Project A/_apis/git/repositories/Repo One/pullrequests?api-version=7.0&searchCriteria.status=active" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "value": [
                            {
                              "pullRequestId": 11,
                              "title": "Repo One PR",
                              "createdBy": {
                                "displayName": "Dana"
                              },
                              "sourceRefName": "refs/heads/feature/repo-one",
                              "targetRefName": "refs/heads/main",
                              "status": "active",
                              "creationDate": "2026-03-18T12:00:00Z",
                              "_links": {
                                "web": {
                                  "href": "https://ado.contoso.local/pr/11"
                                }
                              }
                            }
                          ]
                        }
                        """, Encoding.UTF8, "application/json")
                },
                "https://ado.contoso.local/tfs/DefaultCollection/Project A/_apis/git/repositories/Repo Two/pullrequests?api-version=7.0&searchCriteria.status=active" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "value": [
                            {
                              "pullRequestId": 12,
                              "title": "Repo Two PR",
                              "createdBy": {
                                "displayName": "Lee"
                              },
                              "sourceRefName": "refs/heads/feature/repo-two",
                              "targetRefName": "refs/heads/main",
                              "status": "active",
                              "creationDate": "2026-03-18T13:00:00Z",
                              "_links": {
                                "web": {
                                  "href": "https://ado.contoso.local/pr/12"
                                }
                              }
                            }
                          ]
                        }
                        """, Encoding.UTF8, "application/json")
                },
                _ => throw new Xunit.Sdk.XunitException($"Unexpected Azure DevOps request URI: {requestUri}")
            };
        });

        AzureDevOpsSourceControlService service = new AzureDevOpsSourceControlService(new HttpClient(handler));
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServer,
            DisplayName = "Contoso Server",
            ServerUrl = "https://ado.contoso.local/tfs",
            Organization = "DefaultCollection",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        };

        List<IReadOnlyList<PullRequestSummary>> batches = new List<IReadOnlyList<PullRequestSummary>>();
        await foreach (IReadOnlyList<PullRequestSummary> batch in service.StreamPullRequestBatchesAsync(settings, null, null, CancellationToken.None))
        {
            batches.Add(batch);
        }

        Assert.Equal(2, batches.Count);
        Assert.Equal("Repo One", Assert.Single(batches[0]).RepositoryName);
        Assert.Equal("Repo Two", Assert.Single(batches[1]).RepositoryName);
    }

    /// <summary>
    /// GitHub — StreamPullRequestBatchesAsync — YieldsPagedBatches
    /// </summary>
    [Fact]
    public async Task GitHub_StreamPullRequestBatchesAsync_YieldsPagedBatches()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            string requestUri = request.RequestUri?.ToString() ?? string.Empty;
            return requestUri switch
            {
                "https://api.github.com/repos/octo-org/archharness/pulls?state=open&per_page=100&page=1" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        [
                          {
                            "number": 21,
                            "title": "Page One PR",
                            "user": {
                              "login": "octocat"
                            },
                            "head": {
                              "ref": "feature/page-one"
                            },
                            "base": {
                              "ref": "main"
                            },
                            "state": "open",
                            "draft": false,
                            "html_url": "https://github.com/octo-org/archharness/pull/21",
                            "created_at": "2026-03-18T09:00:00Z"
                          }
                        ]
                        """, Encoding.UTF8, "application/json")
                },
                _ when requestUri == "https://api.github.com/repos/octo-org/archharness/pulls?state=open&per_page=100&page=2" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                },
                _ => throw new Xunit.Sdk.XunitException($"Unexpected GitHub request URI: {requestUri}")
            };
        });

        GitHubSourceControlService service = CreateGitHubService(handler);
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        };

        List<IReadOnlyList<PullRequestSummary>> batches = new List<IReadOnlyList<PullRequestSummary>>();
        await foreach (IReadOnlyList<PullRequestSummary> batch in service.StreamPullRequestBatchesAsync(settings, null, "archharness", CancellationToken.None))
        {
            batches.Add(batch);
        }

        Assert.Single(batches);
        Assert.Equal("21", Assert.Single(batches[0]).Id);
    }

    /// <summary>
    /// GitHub — GetPullRequestFilesAsync — MapsProviderStatuses
    /// </summary>
    [Fact]
    public async Task GitHub_GetPullRequestFilesAsync_MapsProviderStatuses()
    {
        string? requestUri = null;
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            requestUri = request.RequestUri?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [
                      {
                        "filename": "src/ArchHarness.App/SourceControl/PullRequestFile.cs",
                        "status": "added"
                      },
                      {
                        "filename": "src/ArchHarness.App/SourceControl/PullRequestSummary.cs",
                        "status": "changed"
                      },
                      {
                        "filename": "src/ArchHarness.Web/Program.cs",
                        "status": "removed"
                      },
                      {
                        "filename": "tests/ArchHarness.App.Tests/Web/WebApiTests.cs",
                        "status": "renamed"
                      }
                    ]
                    """, Encoding.UTF8, "application/json")
            };
        });

        GitHubSourceControlService service = CreateGitHubService(handler);
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        };

        IReadOnlyList<PullRequestFile> files = await service.GetPullRequestFilesAsync(
            settings,
            null,
            "archharness",
            "11",
            CancellationToken.None);

        Assert.Equal("https://api.github.com/repos/octo-org/archharness/pulls/11/files?per_page=100&page=1", requestUri);
        Assert.Collection(
            files,
            file =>
            {
                Assert.Equal("src/ArchHarness.App/SourceControl/PullRequestFile.cs", file.Path);
                Assert.Equal(PullRequestFileChangeTypes.ADDED, file.ChangeType);
            },
            file =>
            {
                Assert.Equal("src/ArchHarness.App/SourceControl/PullRequestSummary.cs", file.Path);
                Assert.Equal(PullRequestFileChangeTypes.MODIFIED, file.ChangeType);
            },
            file =>
            {
                Assert.Equal("src/ArchHarness.Web/Program.cs", file.Path);
                Assert.Equal(PullRequestFileChangeTypes.DELETED, file.ChangeType);
            },
            file =>
            {
                Assert.Equal("tests/ArchHarness.App.Tests/Web/WebApiTests.cs", file.Path);
                Assert.Equal(PullRequestFileChangeTypes.RENAMED, file.ChangeType);
            });
    }

    /// <summary>
    /// AzureDevOpsServer — TestConnectionForProviderConnections — UsesProjectsEndpointAndBasicAuth
    /// </summary>
    [Fact]
    public async Task AzureDevOpsServer_TestConnectionForProviderConnections_UsesProjectsEndpointAndBasicAuth()
    {
        string? requestUri = null;
        AuthenticationHeaderValue? authorization = null;
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            requestUri = request.RequestUri?.ToString();
            authorization = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "count": 1, "value": [] }""", Encoding.UTF8, "application/json")
            };
        });

        AzureDevOpsSourceControlService service = new AzureDevOpsSourceControlService(new HttpClient(handler));
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServer,
            DisplayName = "Contoso Server",
            ServerUrl = "https://ado.contoso.local/tfs",
            Organization = "DefaultCollection",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        };

        ConnectionTestResult result = await service.TestConnectionAsync(settings, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("https://ado.contoso.local/tfs/DefaultCollection/_apis/projects?api-version=6.0", requestUri);
        Assert.NotNull(authorization);
        Assert.Equal("Basic", authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes(":ado-pat")), authorization.Parameter);
    }

    /// <summary>
    /// AzureDevOpsServer — TestConnectionForProviderConnections — DoesNotDuplicateCollectionSegment
    /// </summary>
    [Fact]
    public async Task AzureDevOpsServer_TestConnectionForProviderConnections_DoesNotDuplicateCollectionSegment()
    {
        string? requestUri = null;
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            requestUri = request.RequestUri?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "count": 1, "value": [] }""", Encoding.UTF8, "application/json")
            };
        });

        AzureDevOpsSourceControlService service = new AzureDevOpsSourceControlService(new HttpClient(handler));
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServer,
            DisplayName = "Contoso Server",
            ServerUrl = "https://ado.contoso.local/tfs/DefaultCollection",
            Organization = "DefaultCollection",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        };

        ConnectionTestResult result = await service.TestConnectionAsync(settings, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("https://ado.contoso.local/tfs/DefaultCollection/_apis/projects?api-version=6.0", requestUri);
    }

    /// <summary>
    /// GitHub — TestConnectionForProviderConnections — UsesUserEndpointAndTokenHeader
    /// </summary>
    [Fact]
    public async Task GitHub_TestConnectionForProviderConnections_UsesUserEndpointAndTokenHeader()
    {
        string? requestUri = null;
        AuthenticationHeaderValue? authorization = null;
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            requestUri = request.RequestUri?.ToString();
            authorization = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "login": "octocat" }""", Encoding.UTF8, "application/json")
            };
        });

        GitHubSourceControlService service = CreateGitHubService(handler);
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        };

        ConnectionTestResult result = await service.TestConnectionAsync(settings, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("https://api.github.com/user", requestUri);
        Assert.NotNull(authorization);
        Assert.Equal("token", authorization!.Scheme);
        Assert.Equal("github-pat", authorization.Parameter);
    }

    /// <summary>
    /// GitHub — TestConnectionWithoutPersonalAccessToken — UsesOrganizationEndpointWithoutAuthorizationHeader
    /// </summary>
    [Fact]
    public async Task GitHub_TestConnectionWithoutPersonalAccessToken_UsesOrganizationEndpointWithoutAuthorizationHeader()
    {
        string? requestUri = null;
        AuthenticationHeaderValue? authorization = null;
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            requestUri = request.RequestUri?.ToString();
            authorization = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ \"login\": \"octo-org\" }""", Encoding.UTF8, "application/json")
            };
        });

        GitHubSourceControlService service = CreateGitHubService(handler);
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = null,
            IsEnabled = true
        };

        ConnectionTestResult result = await service.TestConnectionAsync(settings, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("https://api.github.com/orgs/octo-org", requestUri);
        Assert.Null(authorization);
    }

    /// <summary>
    /// GitHub — TestConnectionWithoutPersonalAccessToken — UsesUserEndpointWhenConfigured
    /// </summary>
    [Fact]
    public async Task GitHub_TestConnectionWithoutPersonalAccessToken_UsesUserEndpointWhenConfigured()
    {
        string? requestUri = null;
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            requestUri = request.RequestUri?.ToString();
            return requestUri == "https://api.github.com/users/octocat"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{ "login": "octocat" }""", Encoding.UTF8, "application/json")
                }
                : throw new Xunit.Sdk.XunitException($"Unexpected GitHub request URI: {requestUri}");
        });

        GitHubSourceControlService service = CreateGitHubService(handler);
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octocat",
            GitHubOwnerType = GitHubOwnerType.User,
            PersonalAccessToken = null,
            IsEnabled = true
        };

        ConnectionTestResult result = await service.TestConnectionAsync(settings, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("https://api.github.com/users/octocat", requestUri);
    }

    /// <summary>
    /// GitHub — TestConnectionWithoutPersonalAccessToken — DoesNotReportAuthRejectedWhenUserEndpointReturnsForbidden
    /// </summary>
    [Fact]
    public async Task GitHub_TestConnectionWithoutPersonalAccessToken_DoesNotReportAuthRejectedWhenUserEndpointReturnsForbidden()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://api.github.com/users/octocat", request.RequestUri?.ToString());
            Assert.Null(request.Headers.Authorization);
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{ "message": "API rate limit exceeded" }""", Encoding.UTF8, "application/json")
            };
        });

        GitHubSourceControlService service = CreateGitHubService(handler);
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octocat",
            GitHubOwnerType = GitHubOwnerType.User,
            PersonalAccessToken = null,
            IsEnabled = true
        };

        ConnectionTestResult result = await service.TestConnectionAsync(settings, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("failed without authentication", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authentication was rejected", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// GitHub — GetPullRequestsAsync — UsesUserRepositoriesEndpointWhenConfigured
    /// </summary>
    [Fact]
    public async Task GitHub_GetPullRequestsAsync_UsesUserRepositoriesEndpointWhenConfigured()
    {
        string? repositoryRequestUri = null;
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            string requestUri = request.RequestUri?.ToString() ?? string.Empty;
            if (requestUri.Contains("/repos?type=all&per_page=100&page=1", StringComparison.Ordinal))
            {
                repositoryRequestUri = requestUri;
            }

            return requestUri switch
            {
                "https://api.github.com/users/octocat/repos?type=all&per_page=100&page=1" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        [
                            { "name": "archharness" }
                        ]
                        """, Encoding.UTF8, "application/json")
                },
                "https://api.github.com/repos/octocat/archharness/pulls?state=open&per_page=100&page=1" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        [
                            {
                            "number": 21,
                            "title": "Fix review PR flow",
                            "user": {
                                "login": "octocat"
                            },
                            "head": {
                                "ref": "feature/review-pr"
                            },
                            "base": {
                                "ref": "main"
                            },
                            "state": "open",
                            "draft": false,
                            "html_url": "https://github.com/octocat/archharness/pull/21",
                            "created_at": "2026-03-18T09:00:00Z"
                            }
                        ]
                        """, Encoding.UTF8, "application/json")
                },
                _ => throw new Xunit.Sdk.XunitException($"Unexpected GitHub request URI: {requestUri}")
            };
        });

        GitHubSourceControlService service = CreateGitHubService(handler);
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octocat",
            GitHubOwnerType = GitHubOwnerType.User,
            PersonalAccessToken = null,
            IsEnabled = true
        };

        IReadOnlyList<PullRequestSummary> pullRequests = await service.GetPullRequestsAsync(settings, null, null, CancellationToken.None);

        PullRequestSummary pullRequest = Assert.Single(pullRequests);
        Assert.Equal("21", pullRequest.Id);
        Assert.Equal("octocat", pullRequest.ProjectName);
        Assert.Equal("archharness", pullRequest.RepositoryName);
        Assert.Equal("https://api.github.com/users/octocat/repos?type=all&per_page=100&page=1", repositoryRequestUri);
    }

    /// <summary>
    /// GitHub — GetPullRequestsAsync — DoesNotSendAuthorizationHeaderWhenPersonalAccessTokenIsMissing
    /// </summary>
    [Fact]
    public async Task GitHub_GetPullRequestsAsync_DoesNotSendAuthorizationHeaderWhenPersonalAccessTokenIsMissing()
    {
        AuthenticationHeaderValue? authorization = null;
        StubHttpMessageHandler handler = new StubHttpMessageHandler((request, _) =>
        {
            authorization = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [
                      {
                        "number": 21,
                        "title": "Public repo review",
                        "user": {
                          "login": "octocat"
                        },
                        "head": {
                          "ref": "feature/review-pr"
                        },
                        "base": {
                          "ref": "main"
                        },
                        "state": "open",
                        "draft": false,
                        "html_url": "https://github.com/octo-org/archharness/pull/21",
                        "created_at": "2026-03-18T09:00:00Z"
                      }
                    ]
                    """, Encoding.UTF8, "application/json")
            };
        });

        GitHubSourceControlService service = CreateGitHubService(handler);
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = null,
            IsEnabled = true
        };

        IReadOnlyList<PullRequestSummary> pullRequests = await service.GetPullRequestsAsync(settings, null, "archharness", CancellationToken.None);

        PullRequestSummary pullRequest = Assert.Single(pullRequests);
        Assert.Equal("21", pullRequest.Id);
        Assert.Null(authorization);
    }

    /// <summary>
    /// GitHub — TestConnectionAsync — RedactsSensitiveProviderErrors
    /// </summary>
    [Fact]
    public async Task GitHub_TestConnectionAsync_RedactsSensitiveProviderErrors()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{ "message": "token github-pat is invalid" }""", Encoding.UTF8, "application/json")
            });

        GitHubSourceControlService service = CreateGitHubService(handler);
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        };

        ConnectionTestResult result = await service.TestConnectionAsync(settings, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("authentication was rejected", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("github-pat", result.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// AzureDevOpsServer — TestConnectionAsync — RejectsNonHttpsUrls
    /// </summary>
    [Fact]
    public async Task AzureDevOpsServer_TestConnectionAsync_RejectsNonHttpsUrls()
    {
        AzureDevOpsSourceControlService service = new AzureDevOpsSourceControlService(new HttpClient(new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "count": 1, "value": [] }""", Encoding.UTF8, "application/json")
            })));
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServer,
            DisplayName = "Contoso Server",
            ServerUrl = "http://ado.contoso.local/tfs",
            Organization = "DefaultCollection",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        };

        ConnectionTestResult result = await service.TestConnectionAsync(settings, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Azure DevOps Server URL must use HTTPS.", result.Message);
    }

    /// <summary>
    /// AzureDevOpsServer — GetPullRequestFilesAsync — RedactsSensitiveAuthErrors
    /// </summary>
    [Fact]
    public async Task AzureDevOpsServer_GetPullRequestFilesAsync_RedactsSensitiveAuthErrors()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{ "message": "pat ado-pat was rejected" }""", Encoding.UTF8, "application/json")
            });

        AzureDevOpsSourceControlService service = new AzureDevOpsSourceControlService(new HttpClient(handler));
        ProviderConnectionSettings settings = new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServer,
            DisplayName = "Contoso Server",
            ServerUrl = "https://ado.contoso.local/tfs",
            Organization = "DefaultCollection",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        };

        SourceControlRequestFailedException exception = await Assert.ThrowsAsync<SourceControlRequestFailedException>(() => service.GetPullRequestFilesAsync(
            settings,
            "Harness Project",
            "ArchHarness Repo",
            "42",
            CancellationToken.None));

        Assert.Contains("authentication was rejected", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ado-pat", exception.Message, StringComparison.Ordinal);
    }
}
