using System.Net;
using System.Reflection;
using ArchHarness.Web;

namespace ArchHarness.App.Tests.Web;

public sealed class ProgramApplicationExtensionsTests
{
    [Fact]
    public void IsAllowedRemoteAddress_ReturnsFalseWhenRemoteAddressIsNull()
    {
        bool allowed = ArchHarness.Web.ProgramApplicationExtensions.IsAllowedRemoteAddress(null);

        Assert.False(allowed);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void IsAllowedRemoteAddress_ReturnsTrueForLoopbackAddresses(string address)
    {
        bool allowed = ArchHarness.Web.ProgramApplicationExtensions.IsAllowedRemoteAddress(IPAddress.Parse(address));

        Assert.True(allowed);
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.5")]
    [InlineData("2001:db8::1")]
    public void IsAllowedRemoteAddress_ReturnsFalseForNonLoopbackAddresses(string address)
    {
        bool allowed = ArchHarness.Web.ProgramApplicationExtensions.IsAllowedRemoteAddress(IPAddress.Parse(address));

        Assert.False(allowed);
    }
}