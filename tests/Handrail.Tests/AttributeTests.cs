using Xunit;

namespace Handrail.Tests;

public class AttributeTests
{
    [Fact]
    public void A_tool_is_page_confirmed_and_app_scoped_by_default()
    {
        var a = new WebMcpToolAttribute("search_notes");

        Assert.Equal("search_notes", a.Name);
        Assert.Equal(Confirm.Page, a.Confirm);
        Assert.Equal(WebMcpScope.App, a.Scope);
        Assert.False(a.ReadOnly);
        Assert.False(a.AllowUnconfirmedWrite);
    }
}
