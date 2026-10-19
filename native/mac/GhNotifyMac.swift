// libGhNotifyMac: the few macOS APIs gh_notify needs, behind a plain C API for GhNotify.Platform.Mac (NativeShim.cs).
// Notifications (UNUserNotificationCenter), a global hotkey (Carbon RegisterEventHotKey) and the login item (SMAppService).
// Strings are UTF-8 and only valid for the duration of a call. Return codes: 0 is success, see IMacShim.cs for the rest.
// Build: native/mac/build.sh (universal arm64 + x86_64, macOS 13+).

import AppKit
import Carbon
import Foundation
import ServiceManagement
import UserNotifications

/// Bumped together with NativeShim.ApiVersion whenever this C API changes.
let GHN_API_VERSION: Int32 = 2

@_cdecl("ghn_version")
public func ghn_version() -> Int32 { GHN_API_VERSION }

// MARK: - Notifications

public typealias GhnClickCallback = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void

final class NotificationDelegate: NSObject, UNUserNotificationCenterDelegate {
    var onClick: GhnClickCallback?

    // Show banners even while gh_notify is the active app (the popup is open).
    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .list, .sound])
    }

    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                withCompletionHandler completionHandler: @escaping () -> Void) {
        defer { completionHandler() }
        guard response.actionIdentifier == UNNotificationDefaultActionIdentifier, let onClick = onClick else { return }
        let tag = response.notification.request.identifier
        let url = response.notification.request.content.userInfo["url"] as? String
        tag.withCString { tagPtr in
            if let url = url { url.withCString { onClick(tagPtr, $0) } } else { onClick(tagPtr, nil) }
        }
    }
}

let notificationDelegate = NotificationDelegate()

/// UNUserNotificationCenter throws an Objective-C exception (which Swift can't catch) when the process has no bundle, e.g. under
/// `dotnet test`. Only use it from the packaged .app.
func notificationsAvailable() -> Bool { Bundle.main.bundleIdentifier != nil }

/// Installs the delegate (clicks, including the one that launched the app) and asks for permission once; -1 without a bundle.
@_cdecl("ghn_notify_init")
public func ghn_notify_init(_ onClick: GhnClickCallback?) -> Int32 {
    guard notificationsAvailable() else { return -1 }
    notificationDelegate.onClick = onClick
    let center = UNUserNotificationCenter.current()
    center.delegate = notificationDelegate
    // The status before asking: "denied" means an earlier answer (or a management profile) blocks it, and macOS won't ask again.
    center.getNotificationSettings { settings in
        let names = ["notDetermined", "denied", "authorized", "provisional", "ephemeral"]
        let i = settings.authorizationStatus.rawValue
        NSLog("gh_notify: notification permission before asking: \(i < names.count ? names[i] : String(i)) (bundle \(Bundle.main.bundleIdentifier ?? "none"))")
    }
    center.requestAuthorization(options: [.alert, .sound]) { granted, error in
        if let error = error { NSLog("gh_notify: notification permission request failed: \(error.localizedDescription)") }
        else if !granted { NSLog("gh_notify: notifications are turned off for gh_notify in System Settings") }
    }
    return 0
}

@_cdecl("ghn_notify_show")
public func ghn_notify_show(_ tag: UnsafePointer<CChar>, _ title: UnsafePointer<CChar>, _ body: UnsafePointer<CChar>,
                            _ url: UnsafePointer<CChar>?, _ silent: Int32) -> Int32 {
    guard notificationsAvailable() else { return -1 }
    let content = UNMutableNotificationContent()
    content.title = String(cString: title)
    content.body = String(cString: body)
    content.sound = silent != 0 ? nil : .default
    if let url = url { content.userInfo = ["url": String(cString: url)] }
    // The same identifier replaces an earlier notification for the item.
    let request = UNNotificationRequest(identifier: String(cString: tag), content: content, trigger: nil)
    UNUserNotificationCenter.current().add(request) { error in
        if let error = error { NSLog("gh_notify: notification failed: \(error.localizedDescription)") }
    }
    return 0
}

@_cdecl("ghn_notify_remove")
public func ghn_notify_remove(_ tag: UnsafePointer<CChar>) {
    guard notificationsAvailable() else { return }
    let ids = [String(cString: tag)]
    let center = UNUserNotificationCenter.current()
    center.removeDeliveredNotifications(withIdentifiers: ids)
    center.removePendingNotificationRequests(withIdentifiers: ids)
}

@_cdecl("ghn_notify_clear_all")
public func ghn_notify_clear_all() {
    guard notificationsAvailable() else { return }
    let center = UNUserNotificationCenter.current()
    center.removeAllDeliveredNotifications()
    center.removeAllPendingNotificationRequests()
}

// MARK: - Global hotkey

public typealias GhnHotkeyCallback = @convention(c) () -> Void

var hotkeyCallback: GhnHotkeyCallback?
var hotkeyRef: EventHotKeyRef?
var hotkeyHandlerInstalled = false
let hotkeySignature: OSType = 0x6768_6E74 // "ghnt"

/// Registers the one gh_notify hotkey (replacing any earlier one). Returns the OSStatus: -9878 (eventHotKeyExistsErr) when another
/// app holds the combination. The callback runs on the main thread.
@_cdecl("ghn_hotkey_register")
public func ghn_hotkey_register(_ keyCode: UInt32, _ modifiers: UInt32, _ onPressed: GhnHotkeyCallback?) -> Int32 {
    ghn_hotkey_unregister()
    hotkeyCallback = onPressed
    if !hotkeyHandlerInstalled {
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let status = InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            var id = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID), nil,
                              MemoryLayout<EventHotKeyID>.size, nil, &id)
            if id.signature == hotkeySignature { hotkeyCallback?() }
            return noErr
        }, 1, &spec, nil, nil)
        if status != noErr { return status }
        hotkeyHandlerInstalled = true
    }
    let id = EventHotKeyID(signature: hotkeySignature, id: 1)
    return RegisterEventHotKey(keyCode, modifiers, id, GetApplicationEventTarget(), 0, &hotkeyRef)
}

@_cdecl("ghn_hotkey_unregister")
public func ghn_hotkey_unregister() {
    if let ref = hotkeyRef { UnregisterEventHotKey(ref) }
    hotkeyRef = nil
}

// MARK: - Login item

/// 0 not registered, 1 enabled, 2 requires approval, 3 not found, -1 unknown.
@_cdecl("ghn_login_item_status")
public func ghn_login_item_status() -> Int32 {
    switch SMAppService.mainApp.status {
    case .notRegistered: return 0
    case .enabled: return 1
    case .requiresApproval: return 2
    case .notFound: return 3
    @unknown default: return -1
    }
}

/// 0 on success, -1 on failure (logged).
@_cdecl("ghn_login_item_set")
public func ghn_login_item_set(_ enabled: Int32) -> Int32 {
    do {
        if enabled != 0 { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
        return 0
    } catch {
        NSLog("gh_notify: login item \(enabled != 0 ? "register" : "unregister") failed: \(error.localizedDescription)")
        return -1
    }
}

// MARK: - Menu-bar icon position

/// The frame of our status item (Avalonia's tray icon) in points, with the origin at the top left of the main screen (Avalonia's
/// screen coordinates on macOS), so the popup can open under the icon. Its button lives in an NSStatusBarWindow that belongs to
/// this process. 0 when found, -1 when not (the popup then opens at the top right). Call on the main thread.
@_cdecl("ghn_status_item_frame")
public func ghn_status_item_frame(_ x: UnsafeMutablePointer<Double>, _ y: UnsafeMutablePointer<Double>,
                                  _ width: UnsafeMutablePointer<Double>, _ height: UnsafeMutablePointer<Double>) -> Int32 {
    guard let window = NSApp?.windows.first(where: { String(describing: type(of: $0)).contains("StatusBarWindow") && $0.isVisible }),
          let main = NSScreen.screens.first else { return -1 }
    let f = window.frame
    x.pointee = f.minX
    y.pointee = main.frame.maxY - f.maxY
    width.pointee = f.width
    height.pointee = f.height
    return 0
}
