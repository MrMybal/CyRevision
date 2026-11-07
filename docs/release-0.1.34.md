# CyRevision 0.1.34 Alpha

[Français](release-0.1.34.fr.md) · [Release and downloads](https://github.com/MrMybal/CyRevision/releases/tag/v0.1.34)

## Changes

- Update the integrated documentation reader to the current CyDoc architecture.
- Provide 86 pages in both English and French, including current workflows, server routes and public plugin API signatures, parameters, return values and examples.
- Add hierarchical topic navigation, a wider sidebar, top-level search with Ctrl+K, full-title tooltips, reading-position restoration and six reader themes.
- Include an interface gallery, screenshots and localized diagrams with in-reader enlargement and keyboard controls.
- Open the appropriate chapter from F1 and contextual help in both Native V2 and Classic, including when no project is selected.
- Preserve legacy documentation links and distribute the complete local reader under Documentation/Documentation.html.
- Separate documentation authoring tools from the packaged manual and generate the reader during application builds.
- Generate identical documentation revisions from Windows and Unix source checkouts.

Windows V2 requires Windows 10/11 x64 and Direct3D 11. Launch
`CyRevision.Native.exe` from the portable ZIP. The **CyRevision (Classic)**
shortcut and `CyRevision.Desktop.exe` remain available; Linux and macOS use
the Avalonia interface.

The V2 interface remains Alpha. Git features are Beta; Sync, Git + Sync, Lore,
Backup and plugin features remain Alpha.

## Reported issues

The published notes for this release do not provide a bug-by-bug list. Consult the [actual issue tracker](https://github.com/MrMybal/CyRevision/issues) for reports and updates; this is not a claim that the release has no bugs.

## Documented limitations

- Alpha features can be incomplete or change between versions. Keep independent backups and validate recovery before production use.
- Native V2 is Windows x64/Direct3D 11 only. Linux and macOS packages use Classic Avalonia, not the native V2 renderer.
- Windows packages are unsigned. macOS bundles use an ad-hoc integrity signature and are not notarized; operating-system trust warnings can appear.
- Offline Unreal package inspection is simplified metadata inspection, not an Unreal Editor rendering. Engine-specific editor workflows require the corresponding host installation.
- Git, Git LFS, optional VPN tools and provider/editor integrations have separate installation or configuration requirements.
- The complete CyDoc manual is bundled locally; no public documentation website is currently linked for this release. Keep Documentation.html and DocData together inside the Documentation folder.

[Installation and requirements](../README.md#installation) · [All release files and checksums](https://github.com/MrMybal/CyRevision/releases/tag/v0.1.34)
