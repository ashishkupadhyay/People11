using System;
using System.Runtime.InteropServices;

namespace People.Interop;

public static unsafe partial class WhatsAppNative
{
    private const string DllName = "whatsapp_bridge.dll";

    [LibraryImport(DllName)]
    public static partial IntPtr wa_create_client();

    [LibraryImport(DllName)]
    public static partial int wa_is_client_ready(IntPtr clientPtr);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int wa_is_authenticated(string dbPath);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int wa_clear_session(string dbPath);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int wa_load_cache(string dbPath);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int wa_authenticate_qr(
        IntPtr clientPtr,
        string dbPath,
        delegate* unmanaged[Cdecl]<IntPtr, void> qrCallback,
        delegate* unmanaged[Cdecl]<void> successCallback,
        delegate* unmanaged[Cdecl]<IntPtr, void> errorCallback);

    [LibraryImport(DllName)]
    public static partial IntPtr wa_get_chats(IntPtr clientPtr);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int wa_mark_chat_read(IntPtr clientPtr, string chatId);

    [LibraryImport(DllName)]
    public static partial IntPtr wa_get_contacts(IntPtr clientPtr);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr wa_get_contact_name(string chatId);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr wa_get_push_name(string chatId);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr wa_get_phone_number(string chatId);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr wa_get_profile_picture(IntPtr clientPtr, string jid, [MarshalAs(UnmanagedType.I1)] bool preview);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr wa_get_messages(IntPtr clientPtr, string chatId);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int wa_chat_ids_match(string id1, string id2);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr wa_canonicalize_chat_id(string chatId);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int wa_is_self_chat(IntPtr clientPtr, string chatId);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr wa_send_message(IntPtr clientPtr, string chatId, string text);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr wa_download_media(IntPtr clientPtr, string chatId, string messageId, string outputPath);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int wa_fetch_older_messages(IntPtr clientPtr, string chatId, int count, string? anchorId, [MarshalAs(UnmanagedType.Bool)] bool anchorFromMe, long anchorTsMs);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int wa_subscribe_presence(IntPtr clientPtr, string chatId);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr wa_get_messages_paginated(IntPtr clientPtr, string chatId, long beforeTs, int limit);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int wa_get_message_count(IntPtr clientPtr, string chatId);

    [LibraryImport(DllName)]
    public static partial int wa_register_message_callback(
        IntPtr clientPtr,
        delegate* unmanaged[Cdecl]<IntPtr, void> callback);

    [LibraryImport(DllName)]
    public static partial int wa_register_sync_progress_callback(
        IntPtr clientPtr,
        delegate* unmanaged[Cdecl]<int, void> callback);

    [LibraryImport(DllName)]
    public static partial int wa_register_chats_updated_callback(
        IntPtr clientPtr,
        delegate* unmanaged[Cdecl]<void> callback);

    [LibraryImport(DllName)]
    public static partial int wa_register_chat_read_callback(
        IntPtr clientPtr,
        delegate* unmanaged[Cdecl]<IntPtr, void> callback);

    [LibraryImport(DllName)]
    public static partial int wa_register_download_progress_callback(
        IntPtr clientPtr,
        delegate* unmanaged[Cdecl]<IntPtr, long, long, void> callback);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr wa_send_media(
        IntPtr clientPtr,
        string chatId,
        string filePath,
        int mediaType,
        string? caption,
        string? fileName);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int wa_send_reaction(
        IntPtr clientPtr,
        string chatId,
        string messageId,
        string? senderId,
        int fromMe,
        string emoji);

    [LibraryImport(DllName)]
    public static partial void wa_destroy_client(IntPtr clientPtr);

    [LibraryImport(DllName)]
    public static partial void wa_free_string(IntPtr ptr);
}
