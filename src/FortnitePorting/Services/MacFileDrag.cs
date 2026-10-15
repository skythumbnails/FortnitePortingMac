using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace FortnitePorting.Services;

/// <summary>
/// Begins a native macOS file drag (an NSDraggingSession whose item is an NSURL pasteboard writer).
///
/// Why this exists: Avalonia 11.3.x cannot originate file drags on macOS. Its
/// ClipboardImpl maps DataFormats.Files / DataFormats.FileNames to the legacy
/// "NSFilenamesPboardType" and writes it onto the NSPasteboardItem that backs the
/// drag session ([NSPasteboardItem setPropertyList:forType:]) — but NSPasteboardItem
/// only accepts UTI types, so drop targets (Finder, Blender) never receive a usable
/// file URL. See AvaloniaUI/Avalonia#10576; fixed upstream only by the clipboard
/// rework (#19347), which is not in the 11.3.x release branch.
///
/// Dragging an NSURL directly makes AppKit expose the file as public.file-url and,
/// via automatic legacy translation, NSFilenamesPboardType — which is what Blender's
/// GHOST/Cocoa drop handling reads.
/// </summary>
public static class MacFileDrag
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(LibObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel, IntPtr a);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr sel, IntPtr a, IntPtr b, IntPtr c);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_str(IntPtr receiver, IntPtr sel, string utf8);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern CGPoint objc_msgSend_Point(IntPtr receiver, IntPtr sel);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern CGPoint objc_msgSend_PointFrom(IntPtr receiver, IntPtr sel, CGPoint point, IntPtr view);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_RectId(IntPtr receiver, IntPtr sel, CGRect rect, IntPtr contents);

    [DllImport(LibObjC, EntryPoint = "objc_allocateClassPair")]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, IntPtr extraBytes);

    [DllImport(LibObjC, EntryPoint = "objc_registerClassPair")]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(LibObjC, EntryPoint = "class_addMethod")]
    private static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X, Y, Width, Height;
    }

    private delegate nuint SourceMaskDelegate(IntPtr self, IntPtr sel, IntPtr session, nint context);

    // Rooted so the unmanaged thunk stays alive for the process lifetime.
    private static readonly SourceMaskDelegate SourceMask = static (_, _, _, _) => 1; // NSDragOperationCopy

    private static IntPtr _dragSource;

    private static IntPtr Sel(string name) => sel_registerName(name);
    private static IntPtr Cls(string name) => objc_getClass(name);
    private static IntPtr NSStr(string s) => objc_msgSend_str(Cls("NSString"), Sel("stringWithUTF8String:"), s);

    private static IntPtr DragSource
    {
        get
        {
            if (_dragSource != IntPtr.Zero) return _dragSource;

            // NSObject subclass implementing the one required NSDraggingSource method.
            var cls = objc_allocateClassPair(Cls("NSObject"), "FPDragSource", IntPtr.Zero);
            class_addMethod(cls,
                Sel("draggingSession:sourceOperationMaskForDraggingContext:"),
                Marshal.GetFunctionPointerForDelegate(SourceMask),
                "Q@:@q");
            objc_registerClassPair(cls);

            _dragSource = objc_msgSend(objc_msgSend(cls, Sel("alloc")), Sel("init"));
            return _dragSource;
        }
    }

    /// <summary>
    /// Starts a native dragging session for <paramref name="filePath"/> from the given top level.
    /// Must be called on the UI thread while a left-mouse drag is in progress.
    /// </summary>
    /// <returns>true if a native dragging session was started.</returns>
    public static bool BeginDrag(TopLevel topLevel, string filePath)
    {
        if (!OperatingSystem.IsMacOS()) return false;

        try
        {
            if (topLevel.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle macHandle) return false;

            var view = macHandle.NSView;
            if (view == IntPtr.Zero) return false;

            var app = objc_msgSend(Cls("NSApplication"), Sel("sharedApplication"));
            var currentEvent = objc_msgSend(app, Sel("currentEvent"));
            if (currentEvent == IntPtr.Zero) return false;

            // beginDraggingSessionWithItems: needs a left-mouse event
            // (NSEventTypeLeftMouseDown = 1, NSEventTypeLeftMouseDragged = 6).
            var eventType = (long)objc_msgSend(currentEvent, Sel("type"));
            if (eventType != 1 && eventType != 6) return false;

            var nsPath = NSStr(filePath);
            var url = objc_msgSend(Cls("NSURL"), Sel("fileURLWithPath:"), nsPath);
            if (url == IntPtr.Zero) return false;

            // NSURL conforms to NSPasteboardWriting, so it can back the dragging item directly.
            var dragItem = objc_msgSend(objc_msgSend(Cls("NSDraggingItem"), Sel("alloc")),
                Sel("initWithPasteboardWriter:"), url);
            if (dragItem == IntPtr.Zero) return false;

            var workspace = objc_msgSend(Cls("NSWorkspace"), Sel("sharedWorkspace"));
            var icon = objc_msgSend(workspace, Sel("iconForFile:"), nsPath);

            var location = objc_msgSend_Point(currentEvent, Sel("locationInWindow"));
            var viewPoint = objc_msgSend_PointFrom(view, Sel("convertPoint:fromView:"), location, IntPtr.Zero);
            objc_msgSend_RectId(dragItem, Sel("setDraggingFrame:contents:"),
                new CGRect { X = viewPoint.X - 16, Y = viewPoint.Y - 16, Width = 32, Height = 32 }, icon);

            var items = objc_msgSend(Cls("NSArray"), Sel("arrayWithObject:"), dragItem);
            var session = objc_msgSend(view, Sel("beginDraggingSessionWithItems:event:source:"),
                items, currentEvent, DragSource);
            return session != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }
}
