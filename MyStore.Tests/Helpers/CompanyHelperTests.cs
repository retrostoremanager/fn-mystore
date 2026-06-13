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
    public void GetCompanyId_WhenJwtPresent_ReturnsCompanyIdFromJwt()
    {
        var context = TestHelpers.CreateMockFunctionContextWithJwt(42);
        var request = TestHelpers.CreateHttpRequestData(context, null);

        var result = CompanyHelper.GetCompanyId(request);

        result.Should().Be(42);
    }

    [Fact]
    public void GetCompanyId_WhenNoJwt_ReturnsNull()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null);

        var result = CompanyHelper.GetCompanyId(request);

        result.Should().BeNull();
    }

    [Fact]
    public void GetCompanyId_IgnoresXCompanyIdHeader_WhenNoJwt()
    {
        // Security: a client-supplied X-Company-Id header must never be trusted for tenant scoping.
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, new Dictionary<string, string>
        {
            ["X-Company-Id"] = "42"
        });

        var result = CompanyHelper.GetCompanyId(request);

        result.Should().BeNull();
    }

    [Fact]
    public void GetCompanyId_IgnoresCompanyIdQueryParam_WhenNoJwt()
    {
        var context = new Mock<FunctionContext>();
        var query = new NameValueCollection { ["companyId"] = "99" };
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, null, query);

        var result = CompanyHelper.GetCompanyId(request);

        result.Should().BeNull();
    }

    [Fact]
    public void GetCompanyId_PrefersJwtOverSpoofedHeader()
    {
        // JWT says company 7; an attacker also sends X-Company-Id: 42. The JWT must win.
        var context = TestHelpers.CreateMockFunctionContextWithJwt(7);
        var request = TestHelpers.CreateHttpRequestData(context, null, new Dictionary<string, string>
        {
            ["X-Company-Id"] = "42"
        });

        var result = CompanyHelper.GetCompanyId(request);

        result.Should().Be(7);
    }

    [Fact]
    public void GetCompanyIdRequired_WhenJwtPresent_ReturnsCompanyIdFromJwt()
    {
        var context = TestHelpers.CreateMockFunctionContextWithJwt(7);
        var request = TestHelpers.CreateHttpRequestData(context, null);

        var result = CompanyHelper.GetCompanyIdRequired(request);

        result.Should().Be(7);
    }

    [Fact]
    public void GetCompanyIdRequired_WhenNoJwt_ThrowsUnauthorizedAccessException()
    {
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null);

        var act = () => CompanyHelper.GetCompanyIdRequired(request);

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void GetCompanyIdRequired_IgnoresXCompanyIdHeader_ThrowsWhenNoJwt()
    {
        // Security regression test: the header alone must NOT satisfy the requirement.
        var context = new Mock<FunctionContext>();
        var request = TestHelpers.CreateHttpRequestData(context.Object, null, new Dictionary<string, string>
        {
            ["X-Company-Id"] = "42"
        });

        var act = () => CompanyHelper.GetCompanyIdRequired(request);

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void GetCompanyIdRequired_PrefersJwtOverSpoofedHeader()
    {
        var context = TestHelpers.CreateMockFunctionContextWithJwt(7);
        var request = TestHelpers.CreateHttpRequestData(context, null, new Dictionary<string, string>
        {
            ["X-Company-Id"] = "42"
        });

        var result = CompanyHelper.GetCompanyIdRequired(request);

        result.Should().Be(7);
    }
}
