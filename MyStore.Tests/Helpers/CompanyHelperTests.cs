using System.Collections.Specialized;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using MyStore.Functions.Helpers;
using Xunit;

namespace MyStore.Tests.Helpers;

public class CompanyHelperTests
{
    [Fact]
    public void GetCompanyId_WhenHeaderPresent_ReturnsCompanyId()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, new Dictionary<string, string>
        {
            ["X-Company-Id"] = "42"
        });

        var result = CompanyHelper.GetCompanyId(request);

        result.Should().Be(42);
    }

    [Fact]
    public void GetCompanyId_WhenQueryParamPresent_ReturnsCompanyId()
    {
        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection { ["companyId"] = "99" };
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, null, query);

        var result = CompanyHelper.GetCompanyId(request);

        result.Should().Be(99);
    }

    [Fact]
    public void GetCompanyId_WhenHeaderAndQueryPresent_PrefersHeader()
    {
        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection { ["companyId"] = "99" };
        var request = TestHelpers.CreateHttpRequestData(context.Object, null,
            new Dictionary<string, string> { ["X-Company-Id"] = "42" }, query);

        var result = CompanyHelper.GetCompanyId(request);

        result.Should().Be(42);
    }

    [Fact]
    public void GetCompanyId_WhenNoCompanyId_ReturnsNull()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = CompanyHelper.GetCompanyId(request);

        result.Should().BeNull();
    }

    [Fact]
    public void GetCompanyIdRequired_WhenCompanyIdPresent_ReturnsCompanyId()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, new Dictionary<string, string>
        {
            ["X-Company-Id"] = "7"
        });

        var result = CompanyHelper.GetCompanyIdRequired(request);

        result.Should().Be(7);
    }

    [Fact]
    public void GetCompanyIdRequired_WhenNoCompanyId_ThrowsUnauthorizedAccessException()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null);

        var act = () => CompanyHelper.GetCompanyIdRequired(request);

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*Company ID is required*");
    }

}
