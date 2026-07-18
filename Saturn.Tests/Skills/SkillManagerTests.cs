using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Skills;
using Xunit;

namespace Saturn.Tests.Skills
{
    [Collection("Configuration")]
    public class SkillManagerTests : SkillTestBase
    {
        [Fact]
        public async Task CreateSkill_PersistsAndRoundTrips()
        {
            var skill = NewSkill("release-checklist", "Steps for cutting a release.", "release checklist", "changelog");
            skill.Description = "How to cut a release";
            skill.ApplyToOrchestrator = true;
            skill.ApplyToSubAgents = false;

            var created = await SkillManager.CreateSkillAsync(skill);

            var loaded = SkillManager.GetAllSkills().Should().ContainSingle().Subject;
            loaded.Id.Should().Be(created.Id);
            loaded.Name.Should().Be("release-checklist");
            loaded.Description.Should().Be("How to cut a release");
            loaded.Content.Should().Be("Steps for cutting a release.");
            loaded.Triggers.Should().BeEquivalentTo(new[] { "release checklist", "changelog" });
            loaded.ApplyToOrchestrator.Should().BeTrue();
            loaded.ApplyToSubAgents.Should().BeFalse();
            loaded.Scope.Should().Be(SkillScope.Global);

            File.Exists(Path.Combine(SkillManager.GlobalSkillsDirectory, $"{created.Id}.json")).Should().BeTrue();
        }

        [Fact]
        public async Task CreateSkill_DuplicateName_ThrowsCaseInsensitively()
        {
            await SkillManager.CreateSkillAsync(NewSkill("my-skill"));

            var act = () => SkillManager.CreateSkillAsync(NewSkill("MY-SKILL"));

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*already exists*");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("bad<name>")]
        [InlineData("quotes\"break")]
        public async Task CreateSkill_InvalidName_Throws(string name)
        {
            var act = () => SkillManager.CreateSkillAsync(NewSkill(name));

            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task CreateSkill_EmptyContent_Throws()
        {
            var act = () => SkillManager.CreateSkillAsync(NewSkill("empty-content", content: "   "));

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*content*");
        }

        [Fact]
        public async Task CreateSkill_ContentTooLong_Throws()
        {
            var act = () => SkillManager.CreateSkillAsync(
                NewSkill("too-long", content: new string('x', SkillManager.MaxContentLength + 1)));

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*too long*");
        }

        [Fact]
        public async Task CreateSkill_NormalizesTriggersAndTypes()
        {
            var skill = NewSkill("normalize-me", "content", "  lockfile  ", "LOCKFILE", "", "other");
            skill.SubAgentTypes = new List<string> { " coder ", "" };

            var created = await SkillManager.CreateSkillAsync(skill);

            created.Triggers.Should().BeEquivalentTo(new[] { "lockfile", "other" });
            created.SubAgentTypes.Should().BeEquivalentTo(new[] { "coder" });

            var emptyTypes = NewSkill("empty-types");
            emptyTypes.SubAgentTypes = new List<string> { "  " };
            (await SkillManager.CreateSkillAsync(emptyTypes)).SubAgentTypes.Should().BeNull();
        }

        [Fact]
        public async Task UpdateSkill_PersistsChangesAndKeepsCreatedAt()
        {
            var created = await SkillManager.CreateSkillAsync(NewSkill("update-me"));
            var createdAt = created.CreatedAt;

            var edited = created.Clone();
            edited.Content = "new content";
            edited.Description = "new description";
            var updated = await SkillManager.UpdateSkillAsync(edited);

            updated.CreatedAt.Should().Be(createdAt);
            updated.UpdatedAt.Should().BeOnOrAfter(createdAt);

            var loaded = SkillManager.GetSkillById(created.Id)!;
            loaded.Content.Should().Be("new content");
            loaded.Description.Should().Be("new description");
        }

        [Fact]
        public async Task UpdateSkill_RenameToExistingName_Throws()
        {
            await SkillManager.CreateSkillAsync(NewSkill("first"));
            var second = await SkillManager.CreateSkillAsync(NewSkill("second"));

            var edited = second.Clone();
            edited.Name = "First";
            var act = () => SkillManager.UpdateSkillAsync(edited);

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*already exists*");
        }

        [Fact]
        public async Task UpdateSkill_UnknownId_Throws()
        {
            var act = () => SkillManager.UpdateSkillAsync(NewSkill("ghost"));

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not found*");
        }

        [Fact]
        public async Task UpdateSkill_ScopeChange_MovesFileBetweenDirectories()
        {
            var created = await SkillManager.CreateSkillAsync(NewSkill("mover"));
            var globalPath = Path.Combine(SkillManager.GlobalSkillsDirectory, $"{created.Id}.json");
            File.Exists(globalPath).Should().BeTrue();

            var edited = created.Clone();
            edited.Scope = SkillScope.Workspace;
            await SkillManager.UpdateSkillAsync(edited);

            File.Exists(globalPath).Should().BeFalse();
            File.Exists(Path.Combine(SkillManager.WorkspaceSkillsDirectory, $"{created.Id}.json")).Should().BeTrue();
            SkillManager.GetSkillById(created.Id)!.Scope.Should().Be(SkillScope.Workspace);
        }

        [Fact]
        public async Task DeleteSkill_RemovesFile()
        {
            var created = await SkillManager.CreateSkillAsync(NewSkill("doomed"));

            await SkillManager.DeleteSkillAsync(created.Id);

            SkillManager.GetAllSkills().Should().BeEmpty();
            File.Exists(Path.Combine(SkillManager.GlobalSkillsDirectory, $"{created.Id}.json")).Should().BeFalse();
        }

        [Fact]
        public async Task DeleteSkill_UnknownId_Throws()
        {
            var act = () => SkillManager.DeleteSkillAsync("nope");

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not found*");
        }

        [Fact]
        public async Task DuplicateSkill_GeneratesUniqueCopyNames()
        {
            var original = await SkillManager.CreateSkillAsync(NewSkill("dupe-me", "content", "trigger"));

            var copy1 = await SkillManager.DuplicateSkillAsync(original.Id);
            var copy2 = await SkillManager.DuplicateSkillAsync(original.Id);

            copy1.Name.Should().Be("dupe-me-copy");
            copy2.Name.Should().Be("dupe-me-copy-2");
            copy1.Id.Should().NotBe(original.Id);
            copy1.Content.Should().Be(original.Content);
            copy1.Triggers.Should().BeEquivalentTo(original.Triggers);
        }

        [Fact]
        public async Task WorkspaceSkill_ShadowsGlobalSkillWithSameName()
        {
            await SkillManager.CreateSkillAsync(NewSkill("shadowed", "global content"));

            // Simulate a workspace skill file created independently (e.g. checked
            // into the repo) that collides with a global skill's name.
            var workspaceSkill = NewSkill("shadowed", "workspace content");
            Directory.CreateDirectory(SkillManager.WorkspaceSkillsDirectory);
            File.WriteAllText(
                Path.Combine(SkillManager.WorkspaceSkillsDirectory, $"{workspaceSkill.Id}.json"),
                JsonSerializer.Serialize(workspaceSkill));

            var all = SkillManager.GetAllSkills();
            var winner = all.Should().ContainSingle(s => s.Name == "shadowed").Subject;
            winner.Content.Should().Be("workspace content");
            winner.Scope.Should().Be(SkillScope.Workspace);
        }

        [Fact]
        public void LoadedSkill_IdComesFromFileName_NotFileContent()
        {
            // A skill file shipped inside a cloned repo could carry a crafted Id
            // aimed at escaping the skills directory on save or delete.
            var malicious = NewSkill("innocent-looking");
            malicious.Id = @"..\..\..\evil";
            Directory.CreateDirectory(SkillManager.WorkspaceSkillsDirectory);
            File.WriteAllText(
                Path.Combine(SkillManager.WorkspaceSkillsDirectory, "innocent.json"),
                JsonSerializer.Serialize(malicious));

            var loaded = SkillManager.GetAllSkills().Should().ContainSingle().Subject;
            loaded.Id.Should().Be("innocent");
        }

        [Fact]
        public async Task DeleteSkill_TraversalId_CannotEscapeSkillsDirectory()
        {
            var outsideFile = Path.Combine(TempConfigDir, "victim.json");
            File.WriteAllText(outsideFile, "{}");

            var malicious = NewSkill("escape-artist");
            malicious.Id = @"..\victim";
            Directory.CreateDirectory(SkillManager.GlobalSkillsDirectory);
            File.WriteAllText(
                Path.Combine(SkillManager.GlobalSkillsDirectory, "escape.json"),
                JsonSerializer.Serialize(malicious));

            // Id is overridden by the file name, so delete resolves inside the
            // skills directory and the outside file survives.
            await SkillManager.DeleteSkillAsync("escape");

            File.Exists(outsideFile).Should().BeTrue();
            File.Exists(Path.Combine(SkillManager.GlobalSkillsDirectory, "escape.json")).Should().BeFalse();
        }

        [Fact]
        public async Task DuplicateSkill_MaxLengthName_StaysWithinCap()
        {
            var longName = new string('x', SkillManager.MaxNameLength);
            var original = await SkillManager.CreateSkillAsync(NewSkill(longName));

            var copy = await SkillManager.DuplicateSkillAsync(original.Id);

            copy.Name.Length.Should().BeLessThanOrEqualTo(SkillManager.MaxNameLength);
            copy.Name.Should().EndWith("-copy");
        }

        [Fact]
        public async Task SkillPrompts_SanitizesDescriptionsForCatalogs()
        {
            var skill = NewSkill("sneaky-description");
            skill.Description = "line one\nline two <fake-tag> & more";
            await SkillManager.CreateSkillAsync(skill);

            var section = SkillPrompts.BuildSystemPromptSection(SkillAudience.Orchestrator, null)!;
            section.Should().Contain("sneaky-description: line one line two &lt;fake-tag&gt; &amp; more");
            SkillPrompts.DescribeCatalogForTool().Should().NotContain("<fake-tag>");
        }

        [Fact]
        public async Task GetAllSkills_SkipsMalformedFiles()
        {
            await SkillManager.CreateSkillAsync(NewSkill("healthy"));
            File.WriteAllText(Path.Combine(SkillManager.GlobalSkillsDirectory, "broken.json"), "{ not json");

            SkillManager.GetAllSkills().Should().ContainSingle().Which.Name.Should().Be("healthy");
        }

        [Fact]
        public async Task GetAllSkills_NormalizesAndValidatesHandAuthoredFiles()
        {
            await SkillManager.CreateSkillAsync(NewSkill("healthy"));
            Directory.CreateDirectory(SkillManager.GlobalSkillsDirectory);

            // Explicit null Triggers must not reach the matcher as a null list.
            File.WriteAllText(
                Path.Combine(SkillManager.GlobalSkillsDirectory, "null-triggers.json"),
                "{\"Id\":\"x\",\"Name\":\"null-triggers\",\"Content\":\"body\",\"Enabled\":true,\"Triggers\":null}");

            // Values past the creation limits are held to the same bar and skipped.
            File.WriteAllText(
                Path.Combine(SkillManager.GlobalSkillsDirectory, "oversized.json"),
                JsonSerializer.Serialize(NewSkill("oversized", new string('x', SkillManager.MaxContentLength + 1))));

            var all = SkillManager.GetAllSkills();
            all.Select(s => s.Name).Should().BeEquivalentTo(new[] { "healthy", "null-triggers" });
            all.Single(s => s.Name == "null-triggers").Triggers.Should().NotBeNull();
        }

        [Fact]
        public async Task GetApplicableSkills_FiltersByAudienceTypeAndEnabled()
        {
            var orchestratorOnly = NewSkill("orch-only");
            orchestratorOnly.ApplyToSubAgents = false;
            await SkillManager.CreateSkillAsync(orchestratorOnly);

            var coderOnly = NewSkill("coder-only");
            coderOnly.ApplyToOrchestrator = false;
            coderOnly.SubAgentTypes = new List<string> { "coder" };
            await SkillManager.CreateSkillAsync(coderOnly);

            var disabled = NewSkill("disabled-skill");
            disabled.Enabled = false;
            await SkillManager.CreateSkillAsync(disabled);

            SkillManager.GetApplicableSkills(SkillAudience.Orchestrator, null)
                .Select(s => s.Name).Should().BeEquivalentTo(new[] { "orch-only" });
            SkillManager.GetApplicableSkills(SkillAudience.SubAgent, "coder")
                .Select(s => s.Name).Should().BeEquivalentTo(new[] { "coder-only" });
            SkillManager.GetApplicableSkills(SkillAudience.SubAgent, "explorer").Should().BeEmpty();
            SkillManager.GetApplicableSkills(SkillAudience.SubAgent, null).Should().BeEmpty();
            SkillManager.GetApplicableSkills(SkillAudience.None, null).Should().BeEmpty();
        }

        [Fact]
        public async Task SkillPrompts_SystemPromptSection_ListsApplicableSkills()
        {
            SkillPrompts.BuildSystemPromptSection(SkillAudience.Orchestrator, null).Should().BeNull();

            var skill = NewSkill("catalog-skill");
            skill.Description = "a very useful skill";
            await SkillManager.CreateSkillAsync(skill);

            var section = SkillPrompts.BuildSystemPromptSection(SkillAudience.Orchestrator, null);
            section.Should().StartWith("<skills>");
            section.Should().EndWith("</skills>");
            section.Should().Contain("catalog-skill: a very useful skill");
            section.Should().Contain("load_skill");
        }

        [Fact]
        public async Task SkillPrompts_ToolCatalog_ListsEnabledSkillsOnly()
        {
            SkillPrompts.DescribeCatalogForTool().Should().Contain("no skills are defined yet");

            await SkillManager.CreateSkillAsync(NewSkill("visible-skill"));
            var disabled = NewSkill("hidden-skill");
            disabled.Enabled = false;
            await SkillManager.CreateSkillAsync(disabled);

            var catalog = SkillPrompts.DescribeCatalogForTool();
            catalog.Should().Contain("visible-skill");
            catalog.Should().NotContain("hidden-skill");
        }
    }
}
