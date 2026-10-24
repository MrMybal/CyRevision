#include "cyui/Gallery.h"

#include "cyui/Icons.h"
#include "cyui/Layout.h"
#include "cyui/Theme.h"
#include "cyui/Widgets.h"

#include <imgui.h>

#include <algorithm>
#include <cmath>
#include <cstdio>

namespace cyui {
namespace {

void themeBar()
{
    const auto& themes = builtInThemes();
    static const char* names[16] {};
    auto current = 0;
    const auto count = static_cast<int>(std::min<std::size_t>(themes.size(), 16));
    for (auto i = 0; i < count; ++i) {
        names[i] = themes[static_cast<std::size_t>(i)].name.c_str();
        if (themes[static_cast<std::size_t>(i)].key == currentThemeKey())
            current = i;
    }
    ImGui::AlignTextToFramePadding();
    textDim("Theme");
    ImGui::SameLine();
    if (combo("##theme", &current, names, count, dp(200.0f)))
        setTheme(themes[static_cast<std::size_t>(current)].key.c_str());
    ImGui::SameLine();
    static const char* densities[] { "Compact", "Comfortable" };
    auto dense = density() == Density::compact ? 0 : 1;
    if (segmented("##density", densities, 2, &dense))
        setDensity(dense == 0 ? Density::compact : Density::comfortable);
}

void palettePreview()
{
    const auto& p = palette();
    for (const auto& field : paletteFields()) {
        ImGui::PushID(field.key);
        colourChip("##chip", p.*field.member, dp(22.0f, 22.0f));
        tooltip(field.label);
        ImGui::PopID();
        ImGui::SameLine(0.0f, dp(3.0f));
    }
    ImGui::NewLine();
}

void textPage()
{
    heading("Heading 1", 1);
    heading("Heading 2", 2);
    heading("Heading 3", 3);
    sectionTitle("Section title");
    overline("OVERLINE LABEL");
    ImGui::TextUnformatted("Primary text");
    textDim("Secondary text");
    textFaint("Faint text, hints");
    textMono("C:/Projects/mono/path.txt");
    ImGui::TextUnformatted("Shortcut");
    ImGui::SameLine();
    kbd("Ctrl+S");
    ImGui::SameLine();
    textDim("Help");
    helpMarker("A help marker: its tooltip explains the option before it.");
    ImGui::SameLine(0.0f, dp(16.0f));
    if (linkText("A link"))
        notify("Link clicked", Tone::info);
    space();
    badge("NEUTRAL");
    ImGui::SameLine();
    badge("ACCENT", Tone::accent);
    ImGui::SameLine();
    badge("OK", Tone::success);
    ImGui::SameLine();
    badge("BETA", Tone::warning);
    ImGui::SameLine();
    badge("ERROR", Tone::error);
    ImGui::SameLine();
    badge("INFO", Tone::info);
    statusDot(palette().success, "Online");
    ImGui::SameLine();
    ImGui::TextUnformatted("Online");
    ImGui::SameLine(0.0f, dp(14.0f));
    statusDot(palette().error, "Offline");
    ImGui::SameLine();
    ImGui::TextUnformatted("Offline");
    space();
    callout(Tone::info, "A callout gives context to a whole panel: what it does, or why it is empty.");
    space(4.0f);
    callout(Tone::warning, "Unsaved changes will be lost when the project closes.");
}

void buttonsPage()
{
    button("Secondary");
    ImGui::SameLine();
    primaryButton("Primary");
    ImGui::SameLine();
    ghostButton("Ghost");
    ImGui::SameLine();
    button("Outline", Variant::outline);
    ImGui::SameLine();
    dangerButton("Delete");
    ImGui::SameLine();
    button("Confirm", Variant::success);

    iconTextButton(icon::save, "Save");
    ImGui::SameLine();
    iconTextButton(icon::folder, "Open", Variant::ghost);
    ImGui::SameLine();
    iconTextButton(icon::add, "New project", Variant::primary);
    ImGui::SameLine();
    {
        ScopedDisabled disabled;
        button("Disabled");
    }

    space();
    sectionTitle("Toolbar");
    static auto tool = 0;
    static auto playing = false;
    static auto loop = true;
    const char* tools[] { icon::edit, icon::crop, icon::zoomIn, icon::touch };
    const char* tips[] { "Brush (B)", "Crop (C)", "Zoom (Z)", "Hand (H)" };
    for (auto i = 0; i < 4; ++i) {
        if (iconButton(tools[i], tips[i], tool == i))
            tool = i;
        ImGui::SameLine(0.0f, dp(2.0f));
    }
    toolbarSeparator();
    if (iconButton(playing ? icon::pause : icon::play, "Play / pause (Space)", playing, palette().success))
        playing = !playing;
    ImGui::SameLine(0.0f, dp(2.0f));
    iconButton(icon::stop, "Stop");
    ImGui::SameLine(0.0f, dp(2.0f));
    if (iconButton(icon::loop, "Loop (L)", loop))
        loop = !loop;

    space();
    sectionTitle("Toggles");
    static bool mute = false, solo = true, arm = false, snap = true, dark = true;
    toggleButton("M", &mute, palette().info, ImVec2(ImGui::GetFrameHeight(), 0.0f), "Mute");
    ImGui::SameLine(0.0f, dp(2.0f));
    toggleButton("S", &solo, palette().warning, ImVec2(ImGui::GetFrameHeight(), 0.0f), "Solo");
    ImGui::SameLine(0.0f, dp(2.0f));
    toggleButton("R", &arm, palette().error, ImVec2(ImGui::GetFrameHeight(), 0.0f), "Arm");
    ImGui::SameLine(0.0f, dp(10.0f));
    toggleButton("Snap", &snap);
    toggleSwitch("Dark mode", &dark);
    ImGui::SameLine(0.0f, dp(16.0f));
    static auto autosave = false;
    toggleSwitch("Autosave", &autosave);

    space();
    sectionTitle("Segmented");
    static auto view = 1;
    const char* views[] { "List", "Grid", "Details" };
    segmented("##view", views, 3, &view);
    static auto unit = 0;
    const char* units[] { "px", "%", "cm", "in" };
    segmented("##unit", units, 4, &unit, -1.0f);
}

void controlsPage()
{
    static float gain = 0.5f, pan = 0.0f, cutoff = 1200.0f, voices = 8.0f;
    knob("##gain", &gain, 0.0f, 1.0f, 0.5f, { 34.0f, 0, "Gain", "%.2f" });
    ImGui::SameLine();
    KnobStyle panStyle { 34.0f, palette().info, "Pan", "%.2f" };
    panStyle.flags = knobBipolar;
    knob("##pan", &pan, -1.0f, 1.0f, 0.0f, panStyle);
    ImGui::SameLine();
    KnobStyle cutoffStyle { 34.0f, palette().success, "Cutoff", "%.0f", "Hz" };
    cutoffStyle.flags = knobLogarithmic;
    cutoffStyle.modulation = 0.5f + 0.4f * static_cast<float>(std::sin(ImGui::GetTime() * 2.0));
    knob("##cutoff", &cutoff, 20.0f, 20000.0f, 1200.0f, cutoffStyle);
    ImGui::SameLine();
    KnobStyle voiceStyle { 34.0f, palette().warning, "Voices", "%.0f" };
    voiceStyle.flags = knobInteger;
    voiceStyle.steps = 15;
    knob("##voices", &voices, 1.0f, 16.0f, 8.0f, voiceStyle);

    static float faderDb = 0.0f;
    fader("##fader", &faderDb, -70.0f, 6.0f, dp(30.0f, 150.0f));
    ImGui::SameLine();
    const auto t = static_cast<float>(ImGui::GetTime());
    const auto level = 0.35f + 0.3f * std::abs(std::sin(t * 1.7f));
    meter("##meter", dp(12.0f, 150.0f), level * 1.2f, level, level * 0.7f, level * 0.6f);
    ImGui::SameLine();
    ImGui::BeginGroup();
    static float opacity = 0.75f, tempo = 120.0f;
    sliderBar("##opacity", &opacity, 0.0f, 1.0f, "Opacity %.2f", dp(220.0f));
    dragValue("##tempo", &tempo, 0.1f, 20.0f, 300.0f, "%.2f BPM", dp(220.0f));
    meter("##hmeter", ImVec2(dp(220.0f), dp(8.0f)), level, level * 0.9f, level * 0.6f, level * 0.5f, false);
    space(4.0f);
    progressBar(std::fmod(t * 0.15f, 1.0f), ImVec2(dp(220.0f), 0.0f));
    progressBar(-1.0f, ImVec2(dp(220.0f), 0.0f), nullptr, Tone::info);
    progressBar(0.62f, ImVec2(dp(220.0f), 0.0f), "Exporting 62 %", Tone::success);
    spinner("##spin");
    ImGui::SameLine();
    textDim("Working...");
    ImGui::EndGroup();

    space();
    sectionTitle("Fields");
    static char search[128] {};
    searchField("##search", search, sizeof(search), "Search effects", dp(260.0f));
    static char name[128] { "Layer 1" };
    textField("##name", name, sizeof(name), "Name", dp(260.0f));
    static auto blend = 0;
    const char* blends[] { "Normal", "Multiply", "Screen", "Overlay" };
    combo("##blend", &blend, blends, 4, dp(260.0f));
}

void containersPage()
{
    if (beginCard("##card1", "Project")) {
        textDim("D:/Projects/MySong.cydaw");
        ImGui::SameLine();
        badge("SAVED", Tone::success);
        space(4.0f);
        primaryButton("Open");
        ImGui::SameLine();
        button("Reveal");
    }
    endCard();
    space(6.0f);
    if (beginCard("##card2", "Update available", 0.0f, Tone::accent)) {
        ImGui::TextWrapped("Version 2.4 brings a new mixer and faster loading.");
        button("Install", Variant::primary);
    }
    endCard();

    space();
    if (beginSection("Transform")) {
        static float position[2] { 120.0f, 64.0f };
        static float rotation = 0.0f;
        static auto visible = true;
        static auto locked = false;
        if (beginProperties("##transform")) {
            property("Position");
            ImGui::DragFloat2("##position", position);
            property("Rotation", "Degrees, clockwise.");
            sliderBar("##rotation", &rotation, -180.0f, 180.0f, "%.1f deg", -1.0f);
            property("Visible");
            toggleSwitch("##visible", &visible);
            property("Locked");
            ImGui::Checkbox("##locked", &locked);
            property("A very long property label that is clipped");
            static auto mode = 0;
            const char* modes[] { "Fast", "Best" };
            segmented("##mode", modes, 2, &mode, -1.0f);
            endProperties();
        }
        endSection();
    }
    if (beginSection("Appearance", false)) {
        static float colour[3] { 0.9f, 0.5f, 0.2f };
        if (beginProperties("##appearance")) {
            property("Colour");
            ImGui::ColorEdit3("##colour", colour);
            endProperties();
        }
        endSection();
    }

    space();
    sectionTitle("Table (stock ImGui, themed)");
    if (ImGui::BeginTable("##table", 3, ImGuiTableFlags_RowBg | ImGuiTableFlags_BordersInnerH | ImGuiTableFlags_Resizable)) {
        ImGui::TableSetupColumn("Name");
        ImGui::TableSetupColumn("Type");
        ImGui::TableSetupColumn("Size");
        ImGui::TableHeadersRow();
        const char* rows[][3] { { "kick.wav", "Audio", "312 kB" }, { "lead.mid", "MIDI", "4 kB" }, { "mix.cydaw", "Project", "6.2 MB" } };
        for (const auto& row : rows) {
            ImGui::TableNextRow();
            for (auto c = 0; c < 3; ++c) {
                ImGui::TableSetColumnIndex(c);
                if (c == 0)
                    ImGui::TextUnformatted(row[c]);
                else
                    textDim("%s", row[c]);
            }
        }
        ImGui::EndTable();
    }

    space();
    static auto showModal = false;
    if (button("Open a modal"))
        showModal = true;
    ImGui::SameLine();
    if (button("Notify"))
        notify("Project saved", Tone::success);
    ImGui::SameLine();
    if (button("Notify error"))
        notify("Could not open the audio device", Tone::error);
    if (beginModal("Delete the layer?", &showModal)) {
        ImGui::TextWrapped("The layer \"Background\" and its masks will be deleted. This cannot be undone.");
        if (modalButtons("Delete", "Cancel", true) == 1)
            notify("Layer deleted", Tone::neutral);
        endModal();
    }

    space();
    if (beginCard("##empty", nullptr, 170.0f))
        emptyState("No layers yet", "Drop an image here, or press Ctrl+N", icon::photo);
    endCard();
}

}  // namespace

void showGallery(bool* open, const int selectTab)
{
    ImGui::SetNextWindowSize(dp(760.0f, 680.0f), ImGuiCond_FirstUseEver);
    if (!ImGui::Begin("CyUI Gallery", open)) {
        ImGui::End();
        return;
    }
    themeBar();
    palettePreview();
    divider(2.0f);
    if (ImGui::BeginTabBar("##galleryTabs")) {
        if (ImGui::BeginTabItem("Text & status", nullptr, selectTab == 0 ? ImGuiTabItemFlags_SetSelected : 0)) {
            textPage();
            ImGui::EndTabItem();
        }
        if (ImGui::BeginTabItem("Buttons", nullptr, selectTab == 1 ? ImGuiTabItemFlags_SetSelected : 0)) {
            buttonsPage();
            ImGui::EndTabItem();
        }
        if (ImGui::BeginTabItem("Controls", nullptr, selectTab == 2 ? ImGuiTabItemFlags_SetSelected : 0)) {
            controlsPage();
            ImGui::EndTabItem();
        }
        if (ImGui::BeginTabItem("Containers", nullptr, selectTab == 3 ? ImGuiTabItemFlags_SetSelected : 0)) {
            containersPage();
            ImGui::EndTabItem();
        }
        ImGui::EndTabBar();
    }
    ImGui::End();
}

void showThemeEditor(bool* open)
{
    ImGui::SetNextWindowSize(dp(420.0f, 560.0f), ImGuiCond_FirstUseEver);
    if (!ImGui::Begin("CyUI Theme editor", open)) {
        ImGui::End();
        return;
    }
    themeBar();
    static char path[260] { "my_theme.cytheme" };
    textField("##path", path, sizeof(path), "File", -dp(120.0f));
    ImGui::SameLine();
    if (button("Save")) {
        if (savePalette(path, palette()))
            notify("Theme saved", Tone::success);
        else
            notify("Could not save the theme", Tone::error);
    }
    ImGui::SameLine();
    if (button("Load")) {
        auto loaded = palette();
        if (loadPalette(path, loaded)) {
            setPalette(loaded);
            notify("Theme loaded", Tone::success);
        } else {
            notify("Could not read the theme", Tone::error);
        }
    }
    static const char* groups[] { "Grounds", "Lines", "Text", "Accent", "Meaning" };
    auto edited = palette();
    auto changed = false;
    for (auto group = 0; group < 5; ++group) {
        if (!beginSection(groups[group]))
            continue;
        if (beginProperties(groups[group])) {
            for (const auto& field : paletteFields()) {
                if (field.group != group)
                    continue;
                property(field.label);
                auto colour = vec(edited.*field.member);
                ImGui::PushID(field.key);
                if (ImGui::ColorEdit4("##c", &colour.x, ImGuiColorEditFlags_AlphaBar | ImGuiColorEditFlags_DisplayHex)) {
                    edited.*field.member = ImGui::ColorConvertFloat4ToU32(colour);
                    changed = true;
                }
                ImGui::PopID();
            }
            endProperties();
        }
        endSection();
    }
    if (changed)
        setPalette(edited);
    ImGui::End();
}

}  // namespace cyui
