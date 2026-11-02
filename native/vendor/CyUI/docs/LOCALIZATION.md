# Localization

Localization is part of Core, with no optional dependency. English (`en`) is the
default language; French (`fr`) is built in. Built-in module controls use this
catalog, and the C++ and Python galleries provide a live language selector.
The same messages are available to embedded Web views.

## C++

Include `<cyui/Core.h>` or `<cyui/Locale.h>`. Choose a language after creating
the view's ImGui context, before drawing its widgets:

```cpp
cyui::setLocale("fr");                 // omit to keep English
if (cyui::primaryButton(cyui::trLabel("Save").c_str()))
    saveDocument();
cyui::text("%s", cyui::tr("Ready").c_str());
```

Use `tr` for displayed text and `trLabel` for interactive labels, window titles,
tabs and menus. `trLabel("Save")` displays the translated caption and retains
the original ImGui identity. Explicit `###id` and `##suffix` identities are
preserved too. Switching languages therefore keeps selection, focus and edits.
Do not concatenate a translated caption into a new widget ID.

Applications can merge their own messages or register another language:

```cpp
cyui::registerTranslations("fr", {{"app.export", "Exporter"}});
cyui::registerTranslations("de", {{"Save", "Speichern"}}, "Deutsch");
if (cyui::button(cyui::trLabel("app.export", "Export").c_str()))
    exportDocument();
```

`availableLocales()` lists English, French, then additional registered languages.
`locale()` returns the active code. Tags are normalized (`fr_CA` becomes `fr-ca`);
an unregistered regional tag falls back to its registered language (`fr-ca` to
`fr`). A missing message falls back to the base language, a registered English
message, the supplied English fallback, then the key. `setLocale` returns false
for an unavailable language and preserves the current one.

The active C++ language belongs to the current ImGui context. Catalogs are shared.
With no context current, `setLocale` updates the defaults and existing contexts;
do this only while other contexts are not rendering. Register catalogs outside
concurrent rendering as well. Destroying a context releases its locale state.
Store returned strings if their `c_str()` must outlive a function call; do not
retain a pointer to a temporary translation in a model or callback.

## Python

The API is available through `cyui`, or without ImGui through
`cyui_modules.locale`:

```python
import cyui as c
c.set_locale('fr')
if c.primary_button(c.tr_label('Save')):
    save_document()
c.register_translations('fr', {'app.export': 'Exporter'})
label = c.tr('app.export', 'Export')
```

`available_locales()` returns `(code, name)` pairs. `locale_scope('fr')` temporarily
selects a language and restores it on exit. Python uses `ContextVar` state, so the
locale belongs to the execution context, not an ImGui pointer. Tools sharing a
Python context can use `locale_scope` around each view; independent async tasks
may select their own language. Catalogs are shared within the Python interpreter.

## Web and TypeScript

`createCyUIBridge()` exposes an independent `bridge.i18n` catalog. Native
`webThemeJson()` and Python `theme_message()` include the active `locale`.
Send the theme again after changing language, or send a `locale` message with
`payload: {locale: 'fr'}` using the normal versioned bridge envelope.

```html
<button data-cyui-i18n="Save">Save</button>
<input data-cyui-i18n-placeholder="Search...">
```

The bridge translates marked interface text and placeholders and updates the
document language. It leaves unmarked document content and input values alone.
For framework components, use `bridge.i18n.tr(key, englishFallback)` in the
framework's render/update cycle. `createCyUILocale()` also works without the
native bridge. Adding messages or calling `setLocale` directly requires a new
`apply()` or framework render; incoming native language messages call `apply()`.
Copy `cyui-i18n.js` and `cyui-locales.js` with `cyui-web.js` when bundling the Web
bridge. An iframe has its own language and must receive it through its own
protocol; `mountCyMD().open(name, file, locale)` defaults to English.

## Unreal Editor

Call `cyui::setLocale` inside the panel's `RunInContext` callback, or from its draw
callback. Each panel owns a context and can select a different language. Register
application catalogs during tool setup. Native Slate/Unreal menu entries use
Unreal's localization system; CyUI translates the content drawn by CyUI.

## What applications translate

CyUI supplies translations for its own controls, default prompts and gallery
interface. Applications supply translations for their actions, validation
messages and model captions. Inspector property names/help and choice captions,
combo/segmented options, command captions/categories, field prompts, knob captions and tooltips use
the catalog; their stored values and identifiers do not change. Pass English
message keys or registered application keys for these captions.

Free text, file names, asset names, graph contents, source code, diffs, logs,
documents and chat messages remain application data. They are not automatically
translated. Format user content with a fixed format string, such as
`cyui::text("%s", content.c_str())`. Persist the user's language preference in the
application and apply it at startup; CyUI does not choose from the OS language.

Text is UTF-8. The host must load fonts covering the languages it enables; the
Windows demo's fonts cover English and French. CyUI does not supply general
right-to-left layout, complex-script shaping, plural rules or locale-specific
date/number formatting. Those require host support or a dedicated text/formatting
library. English/French UI translation does not imply those features.

## Maintaining the built-in catalogs

Edit `locales/en.json` and `locales/fr.json`, then run:

```sh
python -B tools/generate_locales.py
python -B tools/generate_locales.py --check
```

The generator checks matching keys and printf placeholders and produces the
dependency-free C++, Python and JavaScript catalogs. Keep an English fallback for
every message, preserve stable IDs, and translate interface strings rather than
model data. CI checks that the generated catalogs are current.
