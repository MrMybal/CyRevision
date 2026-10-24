#pragma once

// CyUI - Icon glyphs of Segoe MDL2 Assets (Windows), merged into the interface
// fonts by cyui::loadFonts(). Each is a UTF-8 string usable in any label:
//     ImGui::Button(cyui::icon::save);
//     cyui::iconButton(cyui::icon::play, "Play (Space)");
//     std::string label = std::string(cyui::icon::folder) + "  Open";
// cyui::iconsAvailable() says whether the glyphs can be drawn (Windows only).

namespace cyui::icon {

inline constexpr const char* play = "\xee\x9d\xa8"; // U+E768
inline constexpr const char* pause = "\xee\x9d\xa9"; // U+E769
inline constexpr const char* stop = "\xee\x9c\x9a"; // U+E71A
inline constexpr const char* record = "\xee\x9f\x88"; // U+E7C8
inline constexpr const char* add = "\xee\x9c\x90"; // U+E710
inline constexpr const char* remove = "\xee\x9c\xb8"; // U+E738
inline constexpr const char* trash = "\xee\x9d\x8d"; // U+E74D
inline constexpr const char* settings = "\xee\x9c\x93"; // U+E713
inline constexpr const char* search = "\xee\x9c\xa1"; // U+E721
inline constexpr const char* folder = "\xee\xa2\xb7"; // U+E8B7
inline constexpr const char* openFile = "\xee\xa3\xa5"; // U+E8E5
inline constexpr const char* save = "\xee\x9d\x8e"; // U+E74E
inline constexpr const char* undo = "\xee\x9e\xa7"; // U+E7A7
inline constexpr const char* redo = "\xee\x9e\xa6"; // U+E7A6
inline constexpr const char* volume = "\xee\x9d\xa7"; // U+E767
inline constexpr const char* mute = "\xee\x9d\x8f"; // U+E74F
inline constexpr const char* edit = "\xee\x9c\x8f"; // U+E70F
inline constexpr const char* close = "\xee\x9c\x91"; // U+E711
inline constexpr const char* check = "\xee\x9c\xbe"; // U+E73E
inline constexpr const char* chevronDown = "\xee\x9c\x8d"; // U+E70D
inline constexpr const char* chevronUp = "\xee\x9c\x8e"; // U+E70E
inline constexpr const char* chevronLeft = "\xee\x9d\xab"; // U+E76B
inline constexpr const char* chevronRight = "\xee\x9d\xac"; // U+E76C
inline constexpr const char* more = "\xee\x9c\x92"; // U+E712
inline constexpr const char* refresh = "\xee\x9c\xac"; // U+E72C
inline constexpr const char* zoomIn = "\xee\xa2\xa3"; // U+E8A3
inline constexpr const char* zoomOut = "\xee\x9c\x9f"; // U+E71F
inline constexpr const char* pin = "\xee\x9c\x98"; // U+E718
inline constexpr const char* link = "\xee\x9c\x9b"; // U+E71B
inline constexpr const char* lock = "\xee\x9c\xae"; // U+E72E
inline constexpr const char* filter = "\xee\x9c\x9c"; // U+E71C
inline constexpr const char* loop = "\xee\xa3\xae"; // U+E8EE
inline constexpr const char* microphone = "\xee\x9c\xa0"; // U+E720
inline constexpr const char* headphone = "\xee\x9f\xb6"; // U+E7F6
inline constexpr const char* audio = "\xee\xa3\x96"; // U+E8D6
inline constexpr const char* note = "\xee\xb1\x8f"; // U+EC4F
inline constexpr const char* equalizer = "\xee\xa7\xa9"; // U+E9E9
inline constexpr const char* library = "\xee\xa3\xb1"; // U+E8F1
inline constexpr const char* tag = "\xee\xa3\xac"; // U+E8EC
inline constexpr const char* star = "\xee\x9c\xb4"; // U+E734
inline constexpr const char* starFill = "\xee\x9c\xb5"; // U+E735
inline constexpr const char* view = "\xee\xa2\x90"; // U+E890
inline constexpr const char* copy = "\xee\xa3\x88"; // U+E8C8
inline constexpr const char* paste = "\xee\x9d\xbf"; // U+E77F
inline constexpr const char* cut = "\xee\xa3\x86"; // U+E8C6
inline constexpr const char* importFile = "\xee\xa2\xb5"; // U+E8B5
inline constexpr const char* exportFile = "\xee\xb7\xa1"; // U+EDE1
inline constexpr const char* power = "\xee\x9f\xa8"; // U+E7E8
inline constexpr const char* info = "\xee\xa5\x86"; // U+E946
inline constexpr const char* warning = "\xee\x9e\xba"; // U+E7BA
inline constexpr const char* grid = "\xef\x83\xa2"; // U+F0E2
inline constexpr const char* ruler = "\xee\xb5\x9e"; // U+ED5E
inline constexpr const char* connect = "\xee\x9c\x83"; // U+E703
inline constexpr const char* shuffle = "\xee\xa2\xb1"; // U+E8B1
inline constexpr const char* back = "\xee\x9c\xab"; // U+E72B
inline constexpr const char* forward = "\xee\x9c\xaa"; // U+E72A
inline constexpr const char* previous = "\xee\xa2\x92"; // U+E892
inline constexpr const char* next = "\xee\xa2\x93"; // U+E893
inline constexpr const char* sync = "\xee\xa2\x95"; // U+E895
inline constexpr const char* dockLeft = "\xee\xa4\x8c"; // U+E90C
inline constexpr const char* newWindow = "\xee\xa2\xa7"; // U+E8A7
inline constexpr const char* backToWindow = "\xee\x9c\xbf"; // U+E73F
inline constexpr const char* dockRight = "\xee\xa4\x8d"; // U+E90D
inline constexpr const char* dockBottom = "\xee\xa4\x8e"; // U+E90E
inline constexpr const char* fullScreen = "\xee\x9d\x80"; // U+E740
inline constexpr const char* menu = "\xee\x9c\x80"; // U+E700
inline constexpr const char* touch = "\xee\xa0\x95"; // U+E815
inline constexpr const char* crop = "\xee\x9e\xa8"; // U+E7A8
inline constexpr const char* trim = "\xee\x9e\x8a"; // U+E78A
inline constexpr const char* download = "\xee\xa2\x96"; // U+E896
inline constexpr const char* presets = "\xee\xa3\xb1"; // U+E8F1
inline constexpr const char* keyboard = "\xee\x9d\xa5"; // U+E765
inline constexpr const char* lightbulb = "\xee\xa0\xaf"; // U+E82F
inline constexpr const char* home = "\xee\xa0\x8f"; // U+E80F
inline constexpr const char* globe = "\xee\x9d\xb4"; // U+E774
inline constexpr const char* people = "\xee\x9c\x96"; // U+E716
inline constexpr const char* contact = "\xee\x9d\xbb"; // U+E77B
inline constexpr const char* mail = "\xee\x9c\x95"; // U+E715
inline constexpr const char* calendar = "\xee\x9e\x87"; // U+E787
inline constexpr const char* clock = "\xee\xa0\xa3"; // U+E823
inline constexpr const char* cloud = "\xee\x9d\x93"; // U+E753
inline constexpr const char* upload = "\xee\xa2\x98"; // U+E898
inline constexpr const char* sort = "\xee\xa3\x8b"; // U+E8CB
inline constexpr const char* viewAll = "\xee\xa2\xa9"; // U+E8A9
inline constexpr const char* hide = "\xee\xb4\x9a"; // U+ED1A
inline constexpr const char* error = "\xee\x9e\x83"; // U+E783
inline constexpr const char* help = "\xee\xa2\x97"; // U+E897
inline constexpr const char* brightness = "\xee\x9c\x86"; // U+E706
inline constexpr const char* colour = "\xee\x9e\x90"; // U+E790
inline constexpr const char* flag = "\xee\x9f\x81"; // U+E7C1
inline constexpr const char* heart = "\xee\xad\x91"; // U+EB51
inline constexpr const char* bug = "\xee\xaf\xa8"; // U+EBE8
inline constexpr const char* repair = "\xee\xa4\x8f"; // U+E90F
inline constexpr const char* document = "\xee\xa2\xa5"; // U+E8A5
inline constexpr const char* code = "\xee\xa5\x83"; // U+E943
inline constexpr const char* photo = "\xee\xae\x9f"; // U+EB9F
inline constexpr const char* camera = "\xee\x9c\xa2"; // U+E722
inline constexpr const char* video = "\xee\x9c\x94"; // U+E714
inline constexpr const char* send = "\xee\x9c\xa4"; // U+E724
inline constexpr const char* share = "\xee\x9c\xad"; // U+E72D
inline constexpr const char* rename = "\xee\xa2\xac"; // U+E8AC
inline constexpr const char* tiles = "\xee\xb2\xa5"; // U+ECA5
inline constexpr const char* list = "\xee\xa8\xb7"; // U+EA37
inline constexpr const char* statusCircle = "\xee\xa8\xbb"; // U+EA3B

}  // namespace cyui::icon
