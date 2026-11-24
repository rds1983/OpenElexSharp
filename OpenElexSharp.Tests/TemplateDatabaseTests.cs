using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class TemplateDatabaseTests
{
    [Fact]
    public void FromTemplates_ResolvesParentsAndOverridesPropertySets()
    {
        var parentGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var parentData = TestTemplateBuilder.CreateTemplate(
            "Parent.entity",
            parentGuid,
            Guid.Empty,
            [TestTemplateBuilder.CreatePropertySet(0x12345678, 1, 0, "parent")],
            []);
        var childData = TestTemplateBuilder.CreateTemplate(
            "Child.entity",
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            parentGuid,
            [TestTemplateBuilder.CreatePropertySet(0x12345678, 1, 0, "child")],
            []);
        var parent = GenomeTemplate.Parse("Parent", "data/parent.tpl", parentData);
        var child = GenomeTemplate.Parse("Child", "data/child.tpl", childData);

        var database = TemplateDatabase.FromTemplates([parent, child]);

        Assert.True(database.TryGetByName("Parent", out var resolvedParent));
        Assert.True(database.TryGetByName("Child.entity", out var resolvedChild));
        Assert.Same(resolvedParent, resolvedChild!.Parent);
        Assert.Equal(1, resolvedChild.Depth);
        var effective = Assert.Single(resolvedChild.GetEffectivePropertySets());
        Assert.Equal("child", System.Text.Encoding.Latin1.GetString(effective.Properties[0].Data, 2, 5));
    }

    [Fact]
    public void TryGetByGuid_FindsTemplates()
    {
        var guid = Guid.NewGuid();
        var data = TestTemplateBuilder.CreateTemplate("Parent.entity", guid, Guid.Empty, [], []);
        var template = GenomeTemplate.Parse("Parent", "data/parent.tpl", data);
        var database = TemplateDatabase.FromTemplates([template]);

        Assert.True(database.TryGetByGuid(guid, out var found));
        Assert.Same(template, found);
        Assert.False(database.TryGetByGuid(Guid.NewGuid(), out _));
    }

    [Fact]
    public void TryGetByName_Unknown_ReturnsFalse()
    {
        var database = TemplateDatabase.FromTemplates([]);

        Assert.False(database.TryGetByName("missing", out _));
    }

    [Fact]
    public void FromTemplates_EmptyGuid_IsReported()
    {
        var data = TestTemplateBuilder.CreateTemplate("Parent.entity", Guid.Empty, Guid.Empty, [], []);
        var template = GenomeTemplate.Parse("Parent", "data/parent.tpl", data);

        var database = TemplateDatabase.FromTemplates([template]);

        Assert.Contains(database.Diagnostics, message => message.Contains("empty GUID", StringComparison.Ordinal));
    }

    [Fact]
    public void FromTemplates_NullTemplates_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TemplateDatabase.FromTemplates(null!));
    }
}
