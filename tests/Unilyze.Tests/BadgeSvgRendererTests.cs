using System.Xml.Linq;

namespace Unilyze.Tests;

public sealed class BadgeSvgRendererTests
{
    static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    [Fact]
    public void Render_ProducesWellFormedXml()
    {
        var badge = new ShieldsBadge(1, "code health", "9.2 / 8.5", "brightgreen");
        var svg = BadgeSvgRenderer.Render(badge);

        Assert.NotNull(XDocument.Parse(svg).Root);
    }

    [Fact]
    public void Render_ContainsLabelAndMessage()
    {
        var badge = new ShieldsBadge(1, "maintainability", "82", "green");
        var svg = BadgeSvgRenderer.Render(badge);

        Assert.Contains("maintainability", svg);
        Assert.Contains("82", svg);
    }

    [Fact]
    public void Render_NamedColor_MapsToHex()
    {
        var badge = new ShieldsBadge(1, "x", "y", "brightgreen");
        var svg = BadgeSvgRenderer.Render(badge);

        Assert.Contains("fill=\"#4c1\"", svg);
    }

    [Fact]
    public void Render_HexColor_PassesThrough()
    {
        var badge = new ShieldsBadge(1, "x", "y", "#abc123");
        var svg = BadgeSvgRenderer.Render(badge);

        Assert.Contains("#abc123", svg);
    }

    [Fact]
    public void Render_UnknownColor_FallsBackToLightgrey()
    {
        var badge = new ShieldsBadge(1, "x", "y", "mystery");
        var svg = BadgeSvgRenderer.Render(badge);

        Assert.Contains("#9f9f9f", svg);
    }

    [Fact]
    public void Render_EscapesXmlSpecialChars()
    {
        var badge = new ShieldsBadge(1, "a&b<c>", "ok", "red");
        var svg = BadgeSvgRenderer.Render(badge);

        Assert.Contains("a&amp;b&lt;c&gt;", svg);
        Assert.DoesNotContain("a&b<c>", svg);

        var doc = XDocument.Parse(svg);
        var labelTexts = doc.Descendants(Ns + "text")
            .Where(e => e.Value.StartsWith("a", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, labelTexts.Count);
        foreach (var text in labelTexts)
            Assert.Equal("a&b<c>", text.Value);
    }

    [Fact]
    public void Render_NoNewlines()
    {
        var badge = new ShieldsBadge(1, "label", "message", "blue");
        var svg = BadgeSvgRenderer.Render(badge);

        Assert.DoesNotContain('\n', svg);
    }

    [Fact]
    public void Render_WidthIsSumOfSections()
    {
        var badge = new ShieldsBadge(1, "code health", "9.2 / 8.5", "brightgreen");
        var svg = BadgeSvgRenderer.Render(badge);
        var doc = XDocument.Parse(svg);

        var svgWidth = int.Parse(doc.Root!.Attribute("width")!.Value);
        var rects = doc.Descendants(Ns + "rect").ToList();
        var lw = int.Parse(rects[1].Attribute("width")!.Value);
        var mw = int.Parse(rects[2].Attribute("width")!.Value);

        Assert.Equal(lw + mw, svgWidth);
    }

    [Fact]
    public void Render_KnownInput_ProducesExactGeometry()
    {
        var badge = new ShieldsBadge(1, "code health", "9.2 / 8.5", "brightgreen");
        var svg = BadgeSvgRenderer.Render(badge);
        var doc = XDocument.Parse(svg);

        Assert.Equal("135", doc.Root!.Attribute("width")!.Value);

        var rects = doc.Descendants(Ns + "rect").ToList();
        Assert.Equal("76", rects[1].Attribute("width")!.Value);
        Assert.Equal("#555", rects[1].Attribute("fill")!.Value);
        Assert.Equal("76", rects[2].Attribute("x")!.Value);
        Assert.Equal("59", rects[2].Attribute("width")!.Value);
        Assert.Equal("#4c1", rects[2].Attribute("fill")!.Value);

        var texts = doc.Descendants(Ns + "text").ToList();
        Assert.Equal("380", texts[0].Attribute("x")!.Value);
        Assert.Equal("654", texts[0].Attribute("textLength")!.Value);
        Assert.Equal("150", texts[0].Attribute("y")!.Value);
        Assert.Equal("380", texts[1].Attribute("x")!.Value);
        Assert.Equal("654", texts[1].Attribute("textLength")!.Value);
        Assert.Equal("140", texts[1].Attribute("y")!.Value);
        Assert.Equal("1055", texts[2].Attribute("x")!.Value);
        Assert.Equal("488", texts[2].Attribute("textLength")!.Value);
        Assert.Equal("150", texts[2].Attribute("y")!.Value);
        Assert.Equal("1055", texts[3].Attribute("x")!.Value);
        Assert.Equal("488", texts[3].Attribute("textLength")!.Value);
        Assert.Equal("140", texts[3].Attribute("y")!.Value);
    }

    [Theory]
    [InlineData("🎉")]
    [InlineData("良")]
    public void Render_SurrogatePair_CountsAsSingleRune(string label)
    {
        var badge = new ShieldsBadge(1, label, "x", "blue");
        var svg = BadgeSvgRenderer.Render(badge);
        var doc = XDocument.Parse(svg);

        var rects = doc.Descendants(Ns + "rect").ToList();
        Assert.Equal("21", rects[1].Attribute("width")!.Value);
    }

    [Fact]
    public void Render_EmptyMessage_ProducesValidXml()
    {
        var badge = new ShieldsBadge(1, "label", "", "blue");
        var svg = BadgeSvgRenderer.Render(badge);
        var doc = XDocument.Parse(svg);

        var rects = doc.Descendants(Ns + "rect").ToList();
        Assert.Equal("10", rects[2].Attribute("width")!.Value);
    }

    [Fact]
    public void Render_ColorWithQuote_FallsBackToLightgrey()
    {
        var badge = new ShieldsBadge(1, "x", "y", "#bad\"x");
        var svg = BadgeSvgRenderer.Render(badge);

        Assert.Contains("fill=\"#9f9f9f\"", svg);
        XDocument.Parse(svg);
    }
}
