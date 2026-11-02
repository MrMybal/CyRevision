# Reusable CyUI module for Unreal plugins

`CyUI` is an optional **Editor module** that other Unreal plugins can embed or
share. It supplies the CyUI/ImGui library, the Slate renderer, input, clipboard,
font textures and Unreal texture registry. Loading the module creates no menus,
demo tools or windows. Your plugin owns its layout, commands and state.
Core/CMake/Python consumers do not acquire Unreal dependencies. This integration
is for native C++ editor tools, not UMG, runtime gameplay UI or Blueprints.

## Embed the module in an existing plugin

Prepare a new staging directory (Python 3.10+):

```sh
python tools/package_unreal.py --layout module --output build/unreal-module/CyUI --modules Core Plots Inspector Code Diff
```

`module` is the default layout. Copy that **single CyUI folder** to
`<YourPlugin>/Source/CyUI`. It contains its Build.cs, public headers, private
backend, selected library snapshot, dependencies and MIT notices. It requires no
separate CyUI plugin and no external checkout path.

Append this entry to your plugin's existing `.uplugin` Modules array; the generated
`ModuleDescriptor.json` contains the same entry. Keep your existing module entries:

```json
{ "Name": "CyUI", "Type": "Editor", "LoadingPhase": "PostEngineInit" }
```

Add `"CyUI"` to the dependencies of your plugin's **Editor** module:

```csharp
PrivateDependencyModuleNames.Add("CyUI");
CppStandard = CppStandardVersion.Cpp20;
```

Use PublicDependencyModuleNames instead if your public headers expose CyUI types.
Runtime/gameplay modules must not depend on this Editor module. Regenerate IDE
project files and build the Editor target with UnrealBuildTool.

Inside the module, `Library/src` and third-party implementations retain their
source bytes as `.inl` files. Generated `.cpp` bridges compile each exactly once;
UBT's recursive source discovery cannot compile duplicate implementations. Header
paths and the API remain unchanged. `Library/SOURCE_LAYOUT.json` records the
original-to-packaged paths; the TextEditor hash manifest uses its packaged name.
No engine code is copied. `CYUI_API` remains empty for static CMake integration.

### A plugin you redistribute: unique module name

Two enabled plugins that each embed a module named `CyUI` cannot load together:
Unreal needs one definition per module name. A plugin distributed to others (a
Marketplace/Fab or studio-wide tool) should therefore either depend on the shared
provider plugin below, or embed a copy under its own module name:

```sh
python tools/package_unreal.py --layout module --module-name MyToolCyUI --output build/unreal-module/MyToolCyUI --modules Core Plots
```

Copy the folder to `<YourPlugin>/Source/MyToolCyUI`, list
`{ "Name": "MyToolCyUI", "Type": "Editor", "LoadingPhase": "PostEngineInit" }`
in your `.uplugin` and depend on `"MyToolCyUI"` in Build.cs. The script renames the
Build.cs class/file, the folder, the descriptor and test names; the Build.cs
defines `CYUI_UNREAL_MODULE` (used by `IMPLEMENT_MODULE` and `FCyUIModule::Get()`)
and maps `CYUI_API` to `MYTOOLCYUI_API`. C++ class names and headers are unchanged.

Each embedded copy still contains its own Dear ImGui build and exports it
(`IMGUI_API=CYUI_API`, public `third_party/imgui` include path). A consumer module
must depend on exactly one CyUI module, and must not also use another ImGui plugin's
headers. ImGui itself is not renamespaced: with two copies loaded, Windows keeps
each DLL's symbols separate; Linux/macOS symbol resolution between two copies has
not been validated.

## Share one module between several plugins

A project must contain **one definition of the module named CyUI**. To use several
consumer plugins together, put the module in one common provider plugin and make
the consumers depend on it. Do not copy Source/CyUI into each enabled plugin.

An optional provider container can be prepared with:

```sh
python tools/package_unreal.py --layout plugin --output build/unreal-provider/CyUI --modules All --gallery
```

Copy it to `<Project>/Plugins/CyUI`. In each consuming plugin's `.uplugin`, append
this reference to its existing Plugins array:

```json
{ "Name": "CyUI", "Enabled": true }
```

The consumer's Build.cs still depends on the **module** `CyUI`. The provider
plugin is only a container and adds no interface on its own. If your provider has
a different plugin name, use that name in the Plugins reference; the module name
in Build.cs remains CyUI (or the `--module-name` you packaged it with). Follow
Unreal's project/engine dependency hierarchy.

`--modules Core` is minimal; `All` includes all ordinary CyUI modules. Dependencies
are selected automatically. `--gallery` only includes the gallery API: your plugin
chooses when to call it. CEF and external ImGuizmo are not packaged here. Existing
output folders are refused. Prepare a new staging folder and replace the previous
module deliberately with the editor closed. Restart after backend/library changes;
the shared module disables dynamic unloading.

## Complete consuming-plugin example

`examples/unreal/CyUIToolExample` is a separate plugin with its own Build.cs,
descriptor, Window-menu entry, nomad tab, Slate layout, CyUI draw callback and
teardown. Its descriptor references the shared provider `CyUI`; enable this
example only when you want its interface. It embeds a panel using `MakePanel`,
not a stand-alone CyUI window. With Gallery selected, its own interface can open
the gallery. For embedding in an existing plugin, copy the example's integration
pattern and declare the module inside that plugin as described above.

## Register an editor tool

In your Editor module's `.Build.cs`:

```csharp
PrivateDependencyModuleNames.AddRange(new[] { "Core", "CyUI" });
CppStandard = CppStandardVersion.Cpp20;
```

Register the tool in `StartupModule`, unregister it in `ShutdownModule`:

```cpp
#include "CyUIModule.h"
#include <cyui/Core.h>

void FMyEditorTools::StartupModule()
{
    FCyUIModule::Get().RegisterTool(
        "MyTools.Inspector", NSLOCTEXT("MyTools", "Inspector", "My CyUI Inspector"),
        FOnDrawCyUITool::CreateRaw(this, &FMyEditorTools::DrawInspector));
}

void FMyEditorTools::DrawInspector()
{
    cyui::heading("My editor tool");
    if (cyui::primaryButton("Inspect selection")) {
        // Read the editor selection / request a host action here.
    }
}

void FMyEditorTools::ShutdownModule()
{
    if (FCyUIModule::IsAvailable())
        FCyUIModule::Get().UnregisterTool("MyTools.Inspector");
}
```

`RegisterTool` rejects empty/duplicate IDs and unbound callbacks. `OpenTool(id)`
opens the registered nomad tab. Your plugin adds its own menu/button/command to
call OpenTool; the service does not add a Window menu. Unregistration detaches the callback before
requesting tab closure, so a closed/unloading owner cannot keep drawing.
For an existing Slate layout, `MakePanel(callback)` returns an `SWidget`.
Keep the owning callback valid for its panel's lifetime; cast to `SCyUIPanel` and
call `Detach()` before destroying a raw callback owner.

```cpp
#include "CyUIModule.h"
#include "SCyUIPanel.h"

// During your toolkit/tab construction, keep this as an owning-plugin member:
Panel = StaticCastSharedRef<SCyUIPanel>(FCyUIModule::Get().MakePanel(
    FOnDrawCyUITool::CreateRaw(this, &FMyEditorTools::DrawInspector)));
// Insert Panel.ToSharedRef() in a Slate slot or SDockTab content.
// Before unloading the owning plugin:
if (Panel) { Panel->Detach(); Panel.Reset(); }
```

`CyUIModule.h` exposes the `FCyUIModule` entry point. The older
`CyUIEditorModule.h` / `FCyUIEditorModule` names remain compatible aliases/types.

Both methods accept an optional `FOnInitializeCyUITool` as their last argument.
It runs once per newly created panel with its context current, after the default
fonts are loaded and before the first frame. Use it to merge a host-supplied font
into `cyui::uiFont()` (set `ImFontConfig::MergeMode` and `DstFont`) or configure
additional fonts. `SCyUIPanel` exposes the same `OnInitialize` Slate argument.
Unicode storage/input does not supply missing font glyphs: choose fonts covering
the scripts your tool displays, with appropriate distribution rights.

The connector owns `CreateContext`, `NewFrame`, the root `Begin/End`, `Render` and
notifications. The callback draws **contents**. Do not run another ImGui frame,
destroy/change the context or load fonts from a draw callback. CyUI Gallery may
create its own ordinary ImGui windows inside the panel. Native OS windows and
docking belong to Slate; ImGui multi-viewport OS windows are disabled.

All APIs/callbacks run on the editor game thread. Work that changes assets or the
world must use the owning Unreal tool's transactions, `Modify`, validation and
Undo/Redo. Draw callbacks may run more than once per editor frame; execute actions
on widget events rather than unconditionally. Read/prepare expensive data outside
the paint callback.

Each panel owns an independent ImGui context. Numeric-entry buffers, wheel state
and notifications are scoped to that context and released on context shutdown.
Palette, density, DPI and fonts are per context too: a theme call styles the panel
whose context is current (inside its draw/initialize callback), and a new panel
starts from the defaults set by theme calls made while no ImGui context is current.
From Slate handlers or other code outside the draw callback, use the panel helpers:
`Panel->RunInContext([] { cyui::setTheme("navy"); })`, and
`Panel->Notify("Saved", cyui::Tone::success)` (or `cyui::notify(context, ...)`).
A plain `cyui::notify()` with no current context is dropped. Slate's render
transform handles desktop DPI, while `DisplayFramebufferScale` supplies font
rasterization density; the panel keeps CyUI's logical DPI at 1 and does not
re-apply the style on every paint.

Input routing: each key goes to exactly one side. ImGui receives it (and Slate
sees it handled) while ImGui takes keyboard input: an active item or text field, a
modal, or a focused view that calls `ImGui::SetNextFrameWantCaptureKeyboard(true)`
(as `webPanel` does); plain navigation keys (arrows, Tab, Space, Enter, Escape,
Page/Home/End) also go to ImGui while its keyboard navigation is active. Every
other key goes only to Unreal, so editor shortcuts such as Ctrl+S, Ctrl+Z, Delete
or F7 work over the panel and never run twice. A tool with its own shortcuts
(Delete, Ctrl+C/V in a canvas) must claim the keyboard that way while focused.
Characters go to ImGui only while it takes keyboard input. The mouse wheel is sent
as vertical; ImGui turns Shift+wheel into horizontal scrolling. ImGui's own textures (font atlas) are transient
`UTexture2D`s updated in place, only in the changed rectangle.

## Show Unreal textures

```cpp
const uint64 imageId = FCyUIModule::Get().RegisterTexture(myTexture);
// In the draw callback (or ImageData for CyUI::Images / Viewport):
ImGui::Image(ImTextureRef(ImTextureID(imageId)), cyui::dp(256, 256));
// After the tool stops submitting this image:
FCyUIModule::Get().UnregisterTexture(imageId);
```

Registration holds a strong UObject reference. Unregistration finishes submitted
Slate/render commands before releasing that reference; avoid doing it every paint.
Register/unregister textures outside draw callbacks, after stopping submissions
of the old image, for example in the owning tool's setup/teardown.
ID 0 and stale/unregistered IDs are skipped. Texture IDs from other ImGui or RHI
backends are not valid here. Only ordinary Slate-renderable `UTexture` resources
are supported by this registry; custom GPU pipelines need their own adapter.

## Compatibility and checks

The connector targets Unreal Engine 5.5+ desktop editor builds, Windows/Linux/macOS,
through public Slate APIs. The local engine used for validation is UE 5.8.4 on
Windows x64. Other engine versions and Linux/macOS builds require validation on
the target installation; this is not a claim of completed testing on all engines.

The provider layout and embedded-module layout are covered separately. The
consumer example has its own `CyUI.ConsumerPlugin.EmbeddedPanel` automation test
for dependency loading, panel creation and drawing from a different module's DLL.
The rendering checks include a real Unreal texture, Unicode text entry,
context restoration, detaching from inside a draw callback,
and captures for five themes at both densities.

`tests/test_unreal_package.py` checks selective packaging without Unreal. Native
CyUI tests check multiple contexts. The plugin contains Unreal automation tests
`CyUI.Editor.Lifecycle` and `CyUI.Editor.RenderAndInput`. The rendering test needs
a real Slate renderer, including when launched with `-RenderOffscreen`; `-nullrhi`
cannot validate its texture uploads or captures. Run the tests from Session
Frontend or an isolated editor project. Select `CyUI.` to include the consumer
plugin test as well (it is registered only while that example is enabled):

```sh
UnrealEditor-Cmd YourProject.uproject -unattended -RenderOffscreen -nosound -NoLiveCoding -ExecCmds="Automation RunTests CyUI." -TestExit="Automation Test Queue Empty" -ReportExportPath=Saved/CyUIAutomation
```

Captures are saved in the test project's `Saved/CyUIReview`. The fixture checks
tool registration/unregistration, context restoration, mouse focus, Unicode text
entry and actual Slate screenshots for every built-in palette.

IME composition/candidate placement, accessibility text providers, touch/gamepad
and OS-level ImGui multi-viewports are not implemented by this basic editor
backend. Basic Unicode character events and clipboard text are supported. An
existing third-party Unreal ImGui plugin may use a different ABI/context; do not
share its headers, symbols or texture IDs with this connector.

The generic Web module may be selected, but WebCEF is not packaged into Unreal.
Unreal may already load its own browser runtime; a Web backend for Unreal must
follow that engine's browser lifecycle and version requirements.

CyUI's English/French localization is included through Core. Set the panel's
language within `RunInContext` or its draw callback; separate panel contexts keep
separate languages. Slate tool/menu entries still use Unreal localization. See
[LOCALIZATION.md](LOCALIZATION.md) for the catalog and stable-label API.

## License

CyUI's connector and packaging script remain MIT. Dear ImGui and the optional
TextEditor retain their MIT notices. Unreal Engine remains an external dependency
under [Epic's own license](https://www.unrealengine.com/eula/unreal); it is not redistributed or relicensed by CyUI. Preserve
the plugin's LICENSE and THIRD_PARTY_NOTICES when distributing CyUI code.

API references: [Slate editor window workflow](https://dev.epicgames.com/documentation/en-us/unreal-engine/slate-editor-window-quickstart-guide-for-unreal-engine),
[FSlateVertex](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/SlateCore/FSlateVertex/Make),
[FSlateRenderer](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/SlateCore/FSlateRenderer).
