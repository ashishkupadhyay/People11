using System;
using System.Runtime.InteropServices;

namespace People.Interop;

public static unsafe partial class WhatsAppNative
{
    private const string DllName = "whatsapp_bridge.dll";

    [LibraryImport(DllName)]
    public static partial IntPtr wa_create_client();

    [LibraryImport(DllName)]
    public static partial int wa_authenticate_qr(
        IntPtr clientPtr,
        delegate* unmanaged[Cdecl]<IntPtr, void> qrCallback,
        delegate* unmanaged[Cdecl]<void> successCallback);

    [LibraryImport(DllName)]
    public static partial IntPtr wa_get_chats(IntPtr clientPtr);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr wa_get_messages(IntPtr clientPtr, string chatId);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int wa_send_message(IntPtr clientPtr, string chatId, string text);

    [LibraryImport(DllName)]
    public static partial int wa_register_message_callback(
        IntPtr clientPtr,
        delegate* unmanaged[Cdecl]<IntPtr, void> callback);

    [LibraryImport(DllName)]
    public static partial void wa_destroy_client(IntPtr clientPtr);

    [LibraryImport(DllName)]
    public static partial void wa_free_string(IntPtr ptr);
}
