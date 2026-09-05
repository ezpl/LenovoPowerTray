using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Windows.ApplicationModel.Resources;
using Xunit;

namespace ChargeKeeper.Tests;

/// <summary>
/// Guards the application's own <c>.resw</c> (#152). Two different questions, kept apart:
/// <list type="bullet">
/// <item>Does the compiled resource index actually hold what the file says, at the subtree path
/// <c>x:Uid</c> resolution needs? Answered here — MRT Core opens the built <c>ChargeKeeper.pri</c>
/// beside the test binary, the same mechanism <c>ZeroZero.Mqtt.WinUI</c> uses unpackaged.</item>
/// <item>Does WinUI's markup loader actually apply that index to a rendered control's property at
/// runtime? NOT answered here — that needs a live <see cref="Microsoft.UI.Xaml.Window"/>, which an
/// elevated, single-instance app run from a test host cannot stand up. Unverified on screen; see
/// LOCALISATION.md.</item>
/// </list>
/// </summary>
public class LocalisationResourceTests
{
    /// <summary>Beside the test binary because Tests references ChargeKeeper.csproj as a
    /// ProjectReference, which copies the app's own compiled resource index over like any other
    /// WinUI build output — the same reason ZeroZero.Mqtt.WinUI.pri turns up there too.</summary>
    private static ResourceMap ResourcesMap() =>
        new ResourceManager("ChargeKeeper.pri").MainResourceMap.GetSubtree("Resources");

    [Theory]
    [InlineData("NameLocationTitle/Text", "Name this network location")]
    [InlineData("NameLocationCancelButton/Content", "Cancel")]
    [InlineData("NameLocationSaveButton/Content", "Save")]
    public void CompiledIndex_ResolvesEveryEntryInTheResourceFile(string path, string expected) =>
        // Proves the .resw's entries reach the compiled index at the exact subtree path x:Uid
        // resolution needs (<Uid>/<Property>) — the half of the mechanism a test can reach.
        Assert.Equal(expected, ResourcesMap().GetValue(path).ValueAsString);

    [Fact]
    public void CompiledIndex_HasNoEntryForAKeyThatWasNeverAdded() =>
        // A typo in a future x:Uid would otherwise resolve silently to nothing rather than fail a
        // build — this pins that the map really is scoped to what Resources.resw declares.
        Assert.Throws<COMException>(() => ResourcesMap().GetValue("NotARealKey/Text"));

    /// <summary>Every <c>x:Uid="…"</c> found in the application's own XAML (the shared modules keep
    /// their own resources and are out of scope).</summary>
    private static IEnumerable<(string Uid, string File)> UidsInXaml()
    {
        string root = Path.GetDirectoryName(RepoFiles.Find("ChargeKeeper.csproj"))!;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "UI"), "*.xaml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"x:Uid=""([^""]+)"""))
                yield return (m.Groups[1].Value, Path.GetFileName(file));
    }

    [Fact]
    public void EveryXUidInXamlHasAMatchingReswEntry()
    {
        // The failure mode this guards: a missing .resw entry does not fail the build, it leaves the
        // control's markup-declared value standing (or blank, once that value is removed) — visible
        // only on screen, which this application cannot be watched on. Catch it here instead.
        var doc = XDocument.Load(RepoFiles.Find(Path.Combine("Strings", "en-GB", "Resources.resw")));
        var declaredUids = doc.Root!.Elements("data")
            .Select(d => d.Attribute("name")!.Value.Split('.')[0])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (uid, file) in UidsInXaml())
            Assert.True(declaredUids.Contains(uid),
                $"{file} declares x:Uid=\"{uid}\" with no matching entry in Resources.resw.");
    }

    [Fact]
    public void EveryResourceEntryCarriesAComment() =>
        // The brief a translator gets is the comment; an entry without one is a key and a value with
        // nothing telling a translator what role it plays.
        Assert.All(
            XDocument.Load(RepoFiles.Find(Path.Combine("Strings", "en-GB", "Resources.resw"))).Root!.Elements("data"),
            data => Assert.False(string.IsNullOrWhiteSpace((string?)data.Element("comment")),
                $"{data.Attribute("name")!.Value} has no <comment>."));
}
