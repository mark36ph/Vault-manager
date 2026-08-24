using System.Text.Json;

namespace FactVaultManager.Desktop.Tests;

public sealed class OctopusProductionRegressionTests
{
    [Fact]
    public void ThreeDPrintedVisual_IsSyntheticUnlessRequested()
    {
        var candidate = Candidate(
            "3D Printed Octopus model",
            "https://example.test/models/octopus",
            "");

        var cue = NativeVisualSelectionPolicy.UnrequestedSyntheticRepresentation(
            "octopuses octopus swimming underwater",
            candidate);
        var requestedCue = NativeVisualSelectionPolicy.UnrequestedSyntheticRepresentation(
            "octopus 3d printed model",
            candidate);

        Assert.NotEmpty(cue);
        Assert.Empty(requestedCue);
    }

    [Fact]
    public void OctopusPluralAliases_AreTreatedAsTheSameRequiredSubject()
    {
        Assert.True(NativeVerifiedAssetAcquisitionEngine.CandidateTitleMentionsSubject(
            "Close-up of an octopus showcasing its texture and colors underwater",
            "octopuses"));
        Assert.True(NativeVerifiedAssetAcquisitionEngine.CandidateTitleMentionsSubject(
            "Several octopi moving across a reef",
            "octopuses"));
        Assert.True(NativeVerifiedAssetAcquisitionEngine.SubjectTokensEquivalent("octopuses", "octopus"));
        Assert.True(NativeVerifiedAssetAcquisitionEngine.SubjectTokensEquivalent("octopi", "octopus"));
    }

    [Fact]
    public void DuplicateSubjectAnchor_IsRemovedBeforeBehaviorEvidenceIsDerived()
    {
        const string query = "octopuses octopus swimming underwater";

        Assert.Equal(
            "subject octopus swimming underwater",
            NativeVerifiedAssetAcquisitionEngine.BuildVerificationQuery(query, "octopuses"));
        Assert.Equal(
            "swimming",
            NativeVerifiedAssetAcquisitionEngine.SceneEvidenceSubject(query, "octopuses"));
        Assert.Equal(
            "subject octopus swimming",
            NativeVerifiedAssetAcquisitionEngine.BuildEvidenceVerificationQuery(query, "octopuses", "swimming"));
    }

    [Fact]
    public void ScientificAndLivingBehaviorScenes_RejectPreparedFoodUnlessFoodIsRequested()
    {
        var candidate = Candidate(
            "Fried octopuses on display at a street food shop",
            "https://wordpress.org/photos/photo/13365c5fbd/",
            "street food market stall");

        var anatomyConflict = NativeVisualSelectionPolicy.SceneContradiction(
            "octopuses octopus gills anatomy illustration",
            candidate);
        var swimmingConflict = NativeVisualSelectionPolicy.SceneContradiction(
            "octopuses octopus swimming underwater",
            candidate);
        var foodRequestConflict = NativeVisualSelectionPolicy.SceneContradiction(
            "octopus fried seafood dish",
            candidate);

        Assert.Contains("prepared-food", anatomyConflict, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("living-subject", swimmingConflict, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(foodRequestConflict);
    }

    [Fact]
    public void PlainRender_IsSyntheticUnlessExplicitlyRequested()
    {
        var cue = NativeVerifiedAssetAcquisitionEngine.UnrequestedSyntheticRepresentation(
            "octopus underwater close up",
            "octopus squid underwater render ocean tentacle");
        var requestedCue = NativeVerifiedAssetAcquisitionEngine.UnrequestedSyntheticRepresentation(
            "octopus underwater render",
            "octopus squid underwater render ocean tentacle");

        Assert.Equal("render", cue);
        Assert.Empty(requestedCue);
    }

    [Fact]
    public void SameSourcePage_ProducesSameFamilyKeyAcrossDifferentFiles()
    {
        var first = Candidate(
            "3D Printed Octopus A",
            "https://www.thingiverse.com/thing:110948",
            "",
            id: "first");
        var second = Candidate(
            "3D Printed Octopus B",
            "https://www.thingiverse.com/thing:110948/",
            "",
            id: "second");
        var other = Candidate(
            "Real octopus",
            "https://commons.wikimedia.org/wiki/File:Octopus.jpg",
            "",
            id: "third");

        Assert.Equal(
            NativeVisualSelectionPolicy.SourceFamilyKey(first),
            NativeVisualSelectionPolicy.SourceFamilyKey(second));
        Assert.NotEqual(
            NativeVisualSelectionPolicy.SourceFamilyKey(first),
            NativeVisualSelectionPolicy.SourceFamilyKey(other));
    }

    [Fact]
    public void PortableResolveFiles_RebaseFromInProgressToCompleted()
    {
        var root = Path.Combine(Path.GetTempPath(), "factvault-rebase-" + Guid.NewGuid().ToString("N"));
        var oldFolder = Path.Combine(root, "projects", "In Progress", "Octopuses Have Three Hearts");
        var newFolder = Path.Combine(root, "projects", "Completed", "Octopuses Have Three Hearts");
        var portable = Path.Combine(newFolder, "Resolve", "Portable", "Octopuses Have Three Hearts");
        Directory.CreateDirectory(portable);

        try
        {
            var mediaPath = Path.Combine(
                oldFolder,
                "Resolve",
                "Portable",
                "Octopuses Have Three Hearts",
                "Media",
                "Video",
                "scene.mp4");
            var fcpxml = Path.Combine(portable, "Octopuses Have Three Hearts.fcpxml");
            File.WriteAllText(fcpxml, $"<asset src=\"{new Uri(mediaPath).AbsoluteUri}\" />");

            var manifest = Path.Combine(portable, "package_manifest.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { source = Path.Combine(oldFolder, "ResolveClips", "scene.mp4") }));

            var changed = NativeResolvePortablePathRebaser.Rebase(oldFolder, newFolder);
            var rebasedXml = File.ReadAllText(fcpxml);
            var rebasedManifest = File.ReadAllText(manifest);

            Assert.Equal(2, changed);
            Assert.DoesNotContain("In%20Progress", rebasedXml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Completed", rebasedXml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("In Progress", rebasedManifest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Completed", rebasedManifest, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PortableResolveFiles_RebaseXmlEscapedAmpersandUris()
    {
        var root = Path.Combine(Path.GetTempPath(), "factvault-rebase-" + Guid.NewGuid().ToString("N"));
        var oldFolder = Path.Combine(root, "projects", "In Progress", "Nature & Animals - 001");
        var newFolder = Path.Combine(root, "projects", "Completed", "Nature & Animals - 001");
        var portable = Path.Combine(newFolder, "Resolve", "Portable", "Nature _ Animals");
        Directory.CreateDirectory(portable);

        try
        {
            var mediaPath = Path.Combine(
                oldFolder,
                "Resolve",
                "Portable",
                "Nature _ Animals",
                "Media",
                "Images",
                "quizcard.png");
            var escapedUri = new Uri(mediaPath).AbsoluteUri.Replace("&", "&amp;", StringComparison.Ordinal);
            var fcpxml = Path.Combine(portable, "Nature & Animals.fcpxml");
            File.WriteAllText(fcpxml, $"<asset src=\"{escapedUri}\" />");

            var changed = NativeResolvePortablePathRebaser.Rebase(oldFolder, newFolder);
            var rebasedXml = File.ReadAllText(fcpxml);

            Assert.Equal(1, changed);
            Assert.DoesNotContain("In%20Progress", rebasedXml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Completed", rebasedXml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("&amp;", rebasedXml, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static NativeAssetCandidate Candidate(
        string title,
        string sourcePage,
        string credit,
        string id = "candidate") =>
        new(
            "openverse",
            id,
            "https://example.test/" + id + ".jpg",
            "image",
            title,
            1080,
            1920,
            0,
            1,
            credit,
            "CC0",
            sourcePage);
}
