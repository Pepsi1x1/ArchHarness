using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ArchHarness.App.Copilot;
using GitHub.Copilot.SDK;

namespace ArchHarness.Desktop;

/// <summary>
/// Avalonia desktop implementation of <see cref="ICopilotUserInputBridge"/> that presents a dialog
/// when the Copilot session requires user input.
/// </summary>
public sealed class DesktopCopilotUserInputBridge : ICopilotUserInputBridge
{
    private readonly IUserInputState _state;
    private readonly IDesktopWindowLocator _windowLocator;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    public DesktopCopilotUserInputBridge(IUserInputState state, IDesktopWindowLocator windowLocator)
    {
        this._state = state;
        this._windowLocator = windowLocator;
    }

    /// <inheritdoc />
    public async Task<UserInputResponse> RequestInputAsync(UserInputRequest request)
    {
        await this._gate.WaitAsync();
        try
        {
            this._state.SetAwaiting(request.Question);
            TaskCompletionSource<string?> responseSource = new TaskCompletionSource<string?>();
            await Dispatcher.UIThread.InvokeAsync(() => this.ShowDialog(request, responseSource));

            string? answer = await responseSource.Task;
            if (string.IsNullOrWhiteSpace(answer) && request.Choices is { Count: > 0 })
            {
                answer = request.Choices[0];
            }

            return new UserInputResponse
            {
                Answer = answer ?? string.Empty,
                WasFreeform = true
            };
        }
        finally
        {
            this._state.Clear();
            this._gate.Release();
        }
    }

    private void ShowDialog(UserInputRequest request, TaskCompletionSource<string?> responseSource)
    {
        TextBox answerBox = new TextBox
        {
            Watermark = "Type your answer",
            MinWidth = 360
        };

        Window dialog = new Window
        {
            Title = "Agent Clarification Required",
            Width = 520,
            Height = 340,
            CanResize = false
        };

        StackPanel choicesPanel = new StackPanel { Spacing = 8 };
        if (request.Choices is { Count: > 0 })
        {
            foreach (string choice in request.Choices)
            {
                Button choiceButton = new Button
                {
                    Content = choice,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                choiceButton.Click += (_, _) =>
                {
                    responseSource.TrySetResult(choice);
                    dialog.Close();
                };
                choicesPanel.Children.Add(choiceButton);
            }
        }

        Button submitButton = new Button
        {
            Content = "Submit",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        submitButton.Click += (_, _) =>
        {
            responseSource.TrySetResult(answerBox.Text);
            dialog.Close();
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = request.Question ?? string.Empty,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        FontSize = 16,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold
                    },
                    choicesPanel,
                    answerBox,
                    submitButton
                }
            }
        };

        dialog.Closed += (_, _) =>
        {
            responseSource.TrySetResult(answerBox.Text);
        };

        if (this._windowLocator.MainWindow is Window owner)
        {
            _ = dialog.ShowDialog(owner);
            return;
        }

        dialog.Show();
    }
}