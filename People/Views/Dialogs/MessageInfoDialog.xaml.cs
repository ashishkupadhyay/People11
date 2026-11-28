using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using People.Core.Models;

namespace People.Views.Dialogs;

public sealed record InfoRowItem(string Label, string Value);

public sealed partial class MessageInfoDialog : ContentDialog
{
    public IReadOnlyList<InfoRowItem> Rows { get; }

    public MessageInfoDialog(Message msg)
    {
        Rows = BuildRows(msg);
        InitializeComponent();
    }

    private static IReadOnlyList<InfoRowItem> BuildRows(Message msg)
    {
        var rows = new List<InfoRowItem>();

        string typeStr = msg.Content?.Type.ToString() ?? "Text";
        rows.Add(new InfoRowItem("Type", typeStr));

        string senderStr = msg.IsOutgoing
            ? "You"
            : (!string.IsNullOrWhiteSpace(msg.SenderName) ? msg.SenderName : "Contact");
        rows.Add(new InfoRowItem("From", senderStr));

        if (msg.Timestamp != DateTimeOffset.MinValue && msg.Timestamp != DateTimeOffset.UnixEpoch)
        {
            rows.Add(new InfoRowItem("Date", msg.Timestamp.ToLocalTime().ToString("f")));
        }

        if (msg.IsOutgoing)
        {
            rows.Add(new InfoRowItem("Status", msg.Status.ToString()));
        }

        if (msg.IsEdited)
        {
            rows.Add(new InfoRowItem("Edited", "Yes"));
        }

        if (msg.IsRevoked)
        {
            rows.Add(new InfoRowItem("Status", "Deleted"));
        }

        if (msg.Content?.IsMedia == true || msg.Content?.IsDocument == true)
        {
            if (!string.IsNullOrEmpty(msg.Content.DisplayName))
                rows.Add(new InfoRowItem("File", msg.Content.DisplayName));
            if (!string.IsNullOrEmpty(msg.Content.FormattedSizeAndType))
                rows.Add(new InfoRowItem("Size / Type", msg.Content.FormattedSizeAndType));
            if (!string.IsNullOrEmpty(msg.Content.FormattedDuration))
                rows.Add(new InfoRowItem("Duration", msg.Content.FormattedDuration));
        }

        if (msg.Reactions != null && msg.Reactions.Count > 0)
        {
            var reactionsSummary = string.Join("  ", msg.Reactions.Select(r =>
                $"{r.Emoji} {(r.IsFromMe ? "You" : (!string.IsNullOrWhiteSpace(r.SenderName) ? r.SenderName : "User"))}"));
            rows.Add(new InfoRowItem("Reactions", reactionsSummary));
        }

        return rows;
    }
}
