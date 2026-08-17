@tool
extends EditorPlugin

var panel: VBoxContainer
var status_label: Label

func _enter_tree() -> void:
	panel = VBoxContainer.new()
	panel.name = "CyRevision"
	var title := Label.new()
	title.text = "CyRevision"
	title.add_theme_font_size_override("font_size", 18)
	panel.add_child(title)
	var details := Label.new()
	details.text = "Private project link over loopback. Asset previews are not included in this first version."
	details.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	panel.add_child(details)
	var test_button := Button.new()
	test_button.text = "Test connection"
	test_button.pressed.connect(_test_connection)
	panel.add_child(test_button)
	var notify_button := Button.new()
	notify_button.text = "Notify project change"
	notify_button.pressed.connect(_notify_change)
	panel.add_child(notify_button)
	var open_button := Button.new()
	open_button.text = "Open CyRevision"
	open_button.pressed.connect(_open_cyrevision)
	panel.add_child(open_button)
	status_label = Label.new()
	status_label.text = "Connection not tested."
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	panel.add_child(status_label)
	add_control_to_dock(DOCK_SLOT_RIGHT_UL, panel)

func _exit_tree() -> void:
	if panel:
		remove_control_from_docks(panel)
		panel.queue_free()

func _test_connection() -> void:
	_send_request("status", HTTPClient.METHOD_GET, "")

func _notify_change() -> void:
	_send_request("notify", HTTPClient.METHOD_POST, JSON.stringify({"action": "godot-project-change"}))

func _send_request(route: String, method: HTTPClient.Method, body: String) -> void:
	var settings := _load_settings()
	if settings.is_empty():
		status_label.text = "Link not configured. Install or configure the Godot companion from CyRevision."
		return
	var request := HTTPRequest.new()
	panel.add_child(request)
	request.timeout = 3.0
	request.request_completed.connect(func(_result: int, code: int, _headers: PackedStringArray, _body: PackedByteArray):
		status_label.text = "Connected to CyRevision (HTTP %d)." % code if code >= 200 and code < 300 else "CyRevision connection failed (HTTP %d)." % code
		request.queue_free()
	, CONNECT_ONE_SHOT)
	var headers := PackedStringArray(["Authorization: Bearer " + str(settings.get("token", "")), "Content-Type: application/json"])
	var error := request.request(str(settings.get("url", "")).trim_suffix("/") + "/" + route, headers, method, body)
	if error != OK:
		status_label.text = "Could not start the CyRevision request: %s" % error_string(error)
		request.queue_free()

func _open_cyrevision() -> void:
	var settings := _load_settings()
	var executable := str(settings.get("executablePath", ""))
	if executable.is_empty():
		status_label.text = "CyRevision executable is not configured."
		return
	var result := OS.create_process(executable, PackedStringArray(["--project", ProjectSettings.globalize_path("res://")]))
	status_label.text = "CyRevision opened for this Godot project." if result > 0 else "Could not open CyRevision."

func _load_settings() -> Dictionary:
	var path := "res://.godot/cyrevision/bridge.json"
	if not FileAccess.file_exists(path):
		return {}
	var file := FileAccess.open(path, FileAccess.READ)
	var parsed = JSON.parse_string(file.get_as_text())
	return parsed if parsed is Dictionary else {}
