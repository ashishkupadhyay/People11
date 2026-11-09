using System;
using System.Collections.Generic;
using System.Linq;
using People.Core.Models;

namespace People.Providers.WhatsApp;

public static class WhatsAppTestData
{
    public static string GetTestImagePath()
    {
        string fileName = "winui_gallery_ink_canvas_light.png";
        string diskPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Conversation_Test_Images", fileName);
        if (System.IO.File.Exists(diskPath))
            return diskPath;

        var srcDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Conversation_Test_Images", fileName));
        if (System.IO.File.Exists(srcDir))
            return srcDir;

        return diskPath;
    }

    public static IReadOnlyList<Chat> GetTestingChats()
    {
        var now = DateTimeOffset.UtcNow;
        var imagePath = GetTestImagePath();

        return new List<Chat>
        {
            new Chat
            {
                Id = "sample_wa_sunvalley",
                PlatformId = "WhatsApp",
                Title = "Windows Core Architecture (Sun Valley)",
                IsGroup = true,
                UnreadCount = 2,
                LastMessageTime = now.AddMinutes(-3),
                LastMessage = new Message
                {
                    Id = "sv_msg_4",
                    ChatId = "sample_wa_sunvalley",
                    PlatformId = "WhatsApp",
                    SenderId = "mikhail",
                    SenderName = "Mikhail Parakhin",
                    Content = new MessageContent { Text = "The tabbed File Explorer in Sun Valley 2 (Nickel) and Mica titlebar look incredible!" },
                    Timestamp = now.AddMinutes(-3),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "pavan", SenderName = "Pavan Davuluri", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },
            new Chat
            {
                Id = "sample_wa_fluent",
                PlatformId = "WhatsApp",
                Title = "WinUI & Fluent Design Crew",
                IsGroup = true,
                UnreadCount = 0,
                LastMessageTime = now.AddMinutes(-12),
                LastMessage = new Message
                {
                    Id = "fl_msg_5",
                    ChatId = "sample_wa_fluent",
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent
                    {
                        Type = MessageContentType.Image,
                        Caption = "Testing the WinUI 3 Canvas and InkToolbar rendering in dark/light mode.",
                        ThumbnailUrl = imagePath,
                        LocalFilePath = imagePath
                    },
                    Timestamp = now.AddMinutes(-12),
                    Status = MessageStatus.Read,
                    IsOutgoing = true,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "rudy", SenderName = "Rudy Huyn", IsFromMe = false },
                        new MessageReaction { Emoji = "😮", SenderId = "joe", SenderName = "Joe Belfiore", IsFromMe = false }
                    }
                }
            },
            new Chat
            {
                Id = "sample_wa_pavan",
                PlatformId = "WhatsApp",
                Title = "Pavan Davuluri",
                IsGroup = false,
                UnreadCount = 0,
                LastMessageTime = now.AddMinutes(-25),
                LastMessage = new Message
                {
                    Id = "pavan_msg_4",
                    ChatId = "sample_wa_pavan",
                    PlatformId = "WhatsApp",
                    SenderId = "pavan",
                    SenderName = "Pavan Davuluri",
                    Content = new MessageContent { Text = "The synergy between silicon, Windows 11, and Surface hardware has never been stronger." },
                    Timestamp = now.AddMinutes(-25),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "pavan", SenderName = "Pavan Davuluri", IsFromMe = false }
                    }
                }
            },
            new Chat
            {
                Id = "sample_wa_nickel",
                PlatformId = "WhatsApp",
                Title = "Sun Valley 2 / Nickel Release Squad",
                IsGroup = true,
                UnreadCount = 1,
                LastMessageTime = now.AddHours(-1),
                LastMessage = new Message
                {
                    Id = "nk_msg_3",
                    ChatId = "sample_wa_nickel",
                    PlatformId = "WhatsApp",
                    SenderId = "jen",
                    SenderName = "Jen Gentleman",
                    Content = new MessageContent { Text = "Build 22621 is rolling out to the Dev Channel with taskbar drag & drop restored!" },
                    Timestamp = now.AddHours(-1),
                    Status = MessageStatus.Delivered,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "🎉", SenderId = "brandon", SenderName = "Brandon LeBlanc", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },
            new Chat
            {
                Id = "sample_wa_dave",
                PlatformId = "WhatsApp",
                Title = "Dave Plummer",
                IsGroup = false,
                UnreadCount = 0,
                LastMessageTime = now.AddHours(-2),
                LastMessage = new Message
                {
                    Id = "dp_msg_3",
                    ChatId = "sample_wa_dave",
                    PlatformId = "WhatsApp",
                    SenderId = "dave",
                    SenderName = "Dave Plummer",
                    Content = new MessageContent { Text = "Modern XAML with C# 13 and Windows App SDK is lightyears ahead of old MFC." },
                    Timestamp = now.AddHours(-2),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "😂", SenderId = "dave", SenderName = "Dave Plummer", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },
            new Chat
            {
                Id = "sample_wa_satya",
                PlatformId = "WhatsApp",
                Title = "Satya Nadella",
                IsGroup = false,
                UnreadCount = 0,
                LastMessageTime = now.AddHours(-4),
                LastMessage = new Message
                {
                    Id = "satya_msg_3",
                    ChatId = "sample_wa_satya",
                    PlatformId = "WhatsApp",
                    SenderId = "satya",
                    SenderName = "Satya Nadella",
                    Content = new MessageContent { Text = "Keep pushing the boundaries on performance and human-centric design." },
                    Timestamp = now.AddHours(-4),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "🙏", SenderId = "satya", SenderName = "Satya Nadella", IsFromMe = false }
                    }
                }
            },
            new Chat
            {
                Id = "sample_wa_zac",
                PlatformId = "WhatsApp",
                Title = "Zac Bowden",
                IsGroup = false,
                UnreadCount = 0,
                LastMessageTime = now.AddMinutes(-10),
                LastMessage = new Message
                {
                    Id = "zac_msg_7",
                    ChatId = "sample_wa_zac",
                    PlatformId = "WhatsApp",
                    SenderId = "zac",
                    SenderName = "Zac Bowden",
                    Content = new MessageContent { Text = "The K2 leaks confirming top and vertical taskbar positioning options along with the fluid WinUI shell are getting huge traction on Windows Central!" },
                    Timestamp = now.AddMinutes(-10),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "🚀", SenderId = "zac", SenderName = "Zac Bowden", IsFromMe = false },
                        new MessageReaction { Emoji = "❤️", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },
            new Chat
            {
                Id = "sample_wa_kevin",
                PlatformId = "WhatsApp",
                Title = "Kevin Gallo",
                IsGroup = false,
                UnreadCount = 0,
                LastMessageTime = now.AddHours(-8),
                LastMessage = new Message
                {
                    Id = "kg_msg_2",
                    ChatId = "sample_wa_kevin",
                    PlatformId = "WhatsApp",
                    SenderId = "kevin",
                    SenderName = "Kevin Gallo",
                    Content = new MessageContent { Text = "WinUI 3 decoupled from the OS means app developers can innovate at cloud speed." },
                    Timestamp = now.AddHours(-8),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "👍", SenderId = "kevin", SenderName = "Kevin Gallo", IsFromMe = false }
                    }
                }
            },
            new Chat
            {
                Id = "sample_wa_hiking",
                PlatformId = "WhatsApp",
                Title = "Weekend Hiking & Campfire",
                IsGroup = true,
                UnreadCount = 3,
                LastMessageTime = now.AddHours(-10),
                LastMessage = new Message
                {
                    Id = "hk_msg_4",
                    ChatId = "sample_wa_hiking",
                    PlatformId = "WhatsApp",
                    SenderId = "maya",
                    SenderName = "Maya Lin",
                    Content = new MessageContent { Text = "Trail forecast is sunny and 68°! Don't forget sunscreen and extra water." },
                    Timestamp = now.AddHours(-10),
                    Status = MessageStatus.Delivered,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "maya", SenderName = "Maya Lin", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "alex", SenderName = "Alex Morgan", IsFromMe = false }
                    }
                }
            },
            new Chat
            {
                Id = "sample_wa_sarah",
                PlatformId = "WhatsApp",
                Title = "Sarah Connor",
                IsGroup = false,
                UnreadCount = 0,
                LastMessageTime = now.AddHours(-14),
                LastMessage = new Message
                {
                    Id = "sc_msg_3",
                    ChatId = "sample_wa_sarah",
                    PlatformId = "WhatsApp",
                    SenderId = "sarah",
                    SenderName = "Sarah Connor",
                    Content = new MessageContent { Text = "Let's grab coffee at 3 PM today near the Redmond campus." },
                    Timestamp = now.AddHours(-14),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "☕", SenderId = "sarah", SenderName = "Sarah Connor", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },
            new Chat
            {
                Id = "sample_wa_family",
                PlatformId = "WhatsApp",
                Title = "Family Group",
                IsGroup = true,
                UnreadCount = 0,
                LastMessageTime = now.AddDays(-1),
                LastMessage = new Message
                {
                    Id = "fam_msg_3",
                    ChatId = "sample_wa_family",
                    PlatformId = "WhatsApp",
                    SenderId = "dad",
                    SenderName = "Dad",
                    Content = new MessageContent { Text = "Cedar plank salmon dinner at 6 PM! 🐟" },
                    Timestamp = now.AddDays(-1),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "mom", SenderName = "Mom", IsFromMe = false },
                        new MessageReaction { Emoji = "😋", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },
            new Chat
            {
                Id = "sample_wa_alex",
                PlatformId = "WhatsApp",
                Title = "Alex Morgan",
                IsGroup = false,
                UnreadCount = 0,
                LastMessageTime = now.AddDays(-2),
                LastMessage = new Message
                {
                    Id = "alex_msg_3",
                    ChatId = "sample_wa_alex",
                    PlatformId = "WhatsApp",
                    SenderId = "alex",
                    SenderName = "Alex Morgan",
                    Content = new MessageContent { Text = "Got the tickets for Sunday's match! Section 104 row F." },
                    Timestamp = now.AddDays(-2),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "🔥", SenderId = "alex", SenderName = "Alex Morgan", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            }
        };
    }

    public static IReadOnlyList<Message> GetTestingMessages(string chatId)
    {
        var now = DateTimeOffset.UtcNow;
        var imagePath = GetTestImagePath();

        return chatId switch
        {
            "sample_wa_sunvalley" => new List<Message>
            {
                new Message
                {
                    Id = "sv_msg_1",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "kevin",
                    SenderName = "Kevin Gallo",
                    Content = new MessageContent { Text = "Sun Valley build 22000 introduced the new rounded geometry and centered taskbar." },
                    Timestamp = now.AddMinutes(-30),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "👍", SenderId = "pavan", SenderName = "Pavan Davuluri", IsFromMe = false }
                    }
                },
                new Message
                {
                    Id = "sv_msg_2",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "dave",
                    SenderName = "Dave Plummer",
                    Content = new MessageContent { Text = "Back in '93 when I wrote Task Manager in my basement, we didn't have XAML islands or DirectComposition. Just raw Win32 message loops and GDI blits! 😂" },
                    Timestamp = now.AddMinutes(-20),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "😂", SenderId = "kevin", SenderName = "Kevin Gallo", IsFromMe = false },
                        new MessageReaction { Emoji = "❤️", SenderId = "pavan", SenderName = "Pavan Davuluri", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                },
                new Message
                {
                    Id = "sv_msg_3",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "And now with Nickel (22H2), Task Manager has a full WinUI 3 Fluent redesign with dark mode and search!" },
                    Timestamp = now.AddMinutes(-10),
                    Status = MessageStatus.Read,
                    IsOutgoing = true,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "👏", SenderId = "dave", SenderName = "Dave Plummer", IsFromMe = false },
                        new MessageReaction { Emoji = "😮", SenderId = "mikhail", SenderName = "Mikhail Parakhin", IsFromMe = false }
                    }
                },
                new Message
                {
                    Id = "sv_msg_4",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "mikhail",
                    SenderName = "Mikhail Parakhin",
                    Content = new MessageContent { Text = "The tabbed File Explorer in Sun Valley 2 (Nickel) and Mica titlebar look incredible!" },
                    Timestamp = now.AddMinutes(-3),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "pavan", SenderName = "Pavan Davuluri", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },

            "sample_wa_fluent" => new List<Message>
            {
                new Message
                {
                    Id = "fl_msg_1",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "rudy",
                    SenderName = "Rudy Huyn",
                    Content = new MessageContent { Text = "Remember when Windows Phone 7 had Metro typography and live tiles? Fluent Design brings Mica, Acrylic, and subtle spring motion full circle." },
                    Timestamp = now.AddMinutes(-40),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "joe", SenderName = "Joe Belfiore", IsFromMe = false }
                    }
                },
                new Message
                {
                    Id = "fl_msg_2",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "joe",
                    SenderName = "Joe Belfiore",
                    Content = new MessageContent { Text = "The centered taskbar and rounded window corners in Sun Valley truly made Windows feel calm and personal." },
                    Timestamp = now.AddMinutes(-32),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "🙏", SenderId = "rudy", SenderName = "Rudy Huyn", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                },
                new Message
                {
                    Id = "fl_msg_3",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "Here is the latest InkCanvas integration running inside our WinUI 3 container:" },
                    Timestamp = now.AddMinutes(-20),
                    Status = MessageStatus.Read,
                    IsOutgoing = true
                },
                new Message
                {
                    Id = "fl_msg_4",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent
                    {
                        Type = MessageContentType.Image,
                        Caption = "Testing the WinUI 3 Canvas and InkToolbar rendering in dark/light mode.",
                        ThumbnailUrl = imagePath,
                        LocalFilePath = imagePath
                    },
                    Timestamp = now.AddMinutes(-12),
                    Status = MessageStatus.Read,
                    IsOutgoing = true,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "rudy", SenderName = "Rudy Huyn", IsFromMe = false },
                        new MessageReaction { Emoji = "😮", SenderId = "joe", SenderName = "Joe Belfiore", IsFromMe = false }
                    }
                }
            },

            "sample_wa_pavan" => new List<Message>
            {
                new Message
                {
                    Id = "pavan_msg_1",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "pavan",
                    SenderName = "Pavan Davuluri",
                    Content = new MessageContent { Text = "Leading both the Windows and Surface teams together allows us to build deeply integrated hardware and software." },
                    Timestamp = now.AddHours(-1).AddMinutes(-15),
                    Status = MessageStatus.Read,
                    IsOutgoing = false
                },
                new Message
                {
                    Id = "pavan_msg_2",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "Pavan, how is the team feeling about the next Windows 11 updates with Copilot+ PCs and NPU architecture?" },
                    Timestamp = now.AddHours(-1),
                    Status = MessageStatus.Read,
                    IsOutgoing = true,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "👍", SenderId = "pavan", SenderName = "Pavan Davuluri", IsFromMe = false }
                    }
                },
                new Message
                {
                    Id = "pavan_msg_3",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "pavan",
                    SenderName = "Pavan Davuluri",
                    Content = new MessageContent { Text = "Incredible momentum. We are optimizing the OS kernel and WinUI compositing to take full advantage of neural processing and ARM64 efficiency." },
                    Timestamp = now.AddMinutes(-40),
                    Status = MessageStatus.Read,
                    IsOutgoing = false
                },
                new Message
                {
                    Id = "pavan_msg_4",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "pavan",
                    SenderName = "Pavan Davuluri",
                    Content = new MessageContent { Text = "The synergy between silicon, Windows 11, and Surface hardware has never been stronger." },
                    Timestamp = now.AddMinutes(-25),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "pavan", SenderName = "Pavan Davuluri", IsFromMe = false }
                    }
                }
            },

            "sample_wa_nickel" => new List<Message>
            {
                new Message
                {
                    Id = "nk_msg_1",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "brandon",
                    SenderName = "Brandon LeBlanc",
                    Content = new MessageContent { Text = "Dev Channel flight today! Sun Valley 2 (Nickel) build 22621 is compiling." },
                    Timestamp = now.AddHours(-3),
                    Status = MessageStatus.Read,
                    IsOutgoing = false
                },
                new Message
                {
                    Id = "nk_msg_2",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "Did the taskbar drag and drop fix make the cutoff?" },
                    Timestamp = now.AddHours(-2),
                    Status = MessageStatus.Read,
                    IsOutgoing = true
                },
                new Message
                {
                    Id = "nk_msg_3",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "jen",
                    SenderName = "Jen Gentleman",
                    Content = new MessageContent { Text = "Build 22621 is rolling out to the Dev Channel with taskbar drag & drop restored!" },
                    Timestamp = now.AddHours(-1),
                    Status = MessageStatus.Delivered,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "🎉", SenderId = "brandon", SenderName = "Brandon LeBlanc", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },

            "sample_wa_dave" => new List<Message>
            {
                new Message
                {
                    Id = "dp_msg_1",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "dave",
                    SenderName = "Dave Plummer",
                    Content = new MessageContent { Text = "Did I ever tell you the story of why the Windows Format dialog still has that 32GB FAT32 limit? I picked that number on a rainy Tuesday morning at Microsoft and it's still there 30 years later!" },
                    Timestamp = now.AddHours(-4),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "😂", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                },
                new Message
                {
                    Id = "dp_msg_2",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "Classic Dave! That video on Dave's Garage had millions in stitches. When are you showing off WinUI 3 on the channel?" },
                    Timestamp = now.AddHours(-3),
                    Status = MessageStatus.Read,
                    IsOutgoing = true
                },
                new Message
                {
                    Id = "dp_msg_3",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "dave",
                    SenderName = "Dave Plummer",
                    Content = new MessageContent { Text = "Modern XAML with C# 13 and Windows App SDK is lightyears ahead of old MFC." },
                    Timestamp = now.AddHours(-2),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "😂", SenderId = "dave", SenderName = "Dave Plummer", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },

            "sample_wa_satya" => new List<Message>
            {
                new Message
                {
                    Id = "satya_msg_1",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "satya",
                    SenderName = "Satya Nadella",
                    Content = new MessageContent { Text = "Hit Refresh. The way this People experience bridges cross-platform communication while adhering to Fluent Design principles is fantastic." },
                    Timestamp = now.AddHours(-6),
                    Status = MessageStatus.Read,
                    IsOutgoing = false
                },
                new Message
                {
                    Id = "satya_msg_2",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "Thank you, Satya! Empowering every person and every organization to achieve more." },
                    Timestamp = now.AddHours(-5),
                    Status = MessageStatus.Read,
                    IsOutgoing = true,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "🙏", SenderId = "satya", SenderName = "Satya Nadella", IsFromMe = false }
                    }
                },
                new Message
                {
                    Id = "satya_msg_3",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "satya",
                    SenderName = "Satya Nadella",
                    Content = new MessageContent { Text = "Keep pushing the boundaries on performance and human-centric design." },
                    Timestamp = now.AddHours(-4),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "🙏", SenderId = "satya", SenderName = "Satya Nadella", IsFromMe = false }
                    }
                }
            },

            "sample_wa_zac" => new List<Message>
            {
                // Early 2022 Sun Valley 2 (Nickel) conversation
                new Message
                {
                    Id = "zac_msg_1",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "zac",
                    SenderName = "Zac Bowden",
                    Content = new MessageContent { Text = "Hey! Heard whispers about Project Sun Valley 2 codename 'Nickel'. Are you guys shipping the redesigned volume flyout in the next insider build?" },
                    Timestamp = new DateTimeOffset(2022, 3, 16, 14, 20, 0, TimeSpan.FromHours(-7)),
                    Status = MessageStatus.Read,
                    IsOutgoing = false
                },
                new Message
                {
                    Id = "zac_msg_2",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "Can neither confirm nor deny, Zac! Check the Windows Insider Dev Channel flight notes on Wednesday at 10 AM PST 😉" },
                    Timestamp = new DateTimeOffset(2022, 3, 16, 14, 32, 0, TimeSpan.FromHours(-7)),
                    Status = MessageStatus.Read,
                    IsOutgoing = true,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "😂", SenderId = "zac", SenderName = "Zac Bowden", IsFromMe = false }
                    }
                },
                new Message
                {
                    Id = "zac_msg_3",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "zac",
                    SenderName = "Zac Bowden",
                    Content = new MessageContent { Text = "Haha, always an NDA! Leaked screenshots of the Mica backdrops and tabbed File Explorer look incredible on Windows Central!" },
                    Timestamp = new DateTimeOffset(2022, 3, 16, 14, 45, 0, TimeSpan.FromHours(-7)),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                },

                // Recent conversation about Windows 'K2' improvements & leaks
                new Message
                {
                    Id = "zac_msg_4",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "zac",
                    SenderName = "Zac Bowden",
                    Content = new MessageContent { Text = "Hey! Breaking report dropping on Windows Central tomorrow about the upcoming 'K2' release." },
                    Timestamp = now.AddHours(-2).AddMinutes(-30),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "😮", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                },
                new Message
                {
                    Id = "zac_msg_5",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "zac",
                    SenderName = "Zac Bowden",
                    Content = new MessageContent { Text = "Sources say K2 finally allows changing taskbar position (top, left, and right options returning!), next-gen fluid shell animations, and deeper NPU-accelerated semantic indexing across the entire OS. Can you give me any color on the WinUI 3 decoupling progress for K2?" },
                    Timestamp = now.AddHours(-2).AddMinutes(-15),
                    Status = MessageStatus.Read,
                    IsOutgoing = false
                },
                new Message
                {
                    Id = "zac_msg_6",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "Off the record: K2's focus is all about responsiveness and continuity. The modular shell architecture lets us modernize legacy surfaces with native WinUI 3 without breaking Win32 compatibility." },
                    Timestamp = now.AddHours(-1).AddMinutes(-40),
                    Status = MessageStatus.Read,
                    IsOutgoing = true,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "🔥", SenderId = "zac", SenderName = "Zac Bowden", IsFromMe = false },
                        new MessageReaction { Emoji = "👏", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                },
                new Message
                {
                    Id = "zac_msg_7",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "zac",
                    SenderName = "Zac Bowden",
                    Content = new MessageContent { Text = "The K2 leaks confirming top and vertical taskbar positioning options along with the fluid WinUI shell are getting huge traction on Windows Central!" },
                    Timestamp = now.AddMinutes(-10),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "🚀", SenderId = "zac", SenderName = "Zac Bowden", IsFromMe = false },
                        new MessageReaction { Emoji = "❤️", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },

            "sample_wa_hiking" => new List<Message>
            {
                new Message
                {
                    Id = "hk_msg_1",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "sarah",
                    SenderName = "Sarah Connor",
                    Content = new MessageContent { Text = "Are we still doing Mount Si this Saturday?" },
                    Timestamp = now.AddHours(-12),
                    Status = MessageStatus.Read,
                    IsOutgoing = false
                },
                new Message
                {
                    Id = "hk_msg_2",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "alex",
                    SenderName = "Alex Morgan",
                    Content = new MessageContent { Text = "Count me in! I'll bring the trail mix and portable stove." },
                    Timestamp = now.AddHours(-11).AddMinutes(-30),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "👍", SenderId = "sarah", SenderName = "Sarah Connor", IsFromMe = false }
                    }
                },
                new Message
                {
                    Id = "hk_msg_3",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "I've packed the first aid kit and extra navigation power banks." },
                    Timestamp = now.AddHours(-11),
                    Status = MessageStatus.Read,
                    IsOutgoing = true
                },
                new Message
                {
                    Id = "hk_msg_4",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "maya",
                    SenderName = "Maya Lin",
                    Content = new MessageContent { Text = "Trail forecast is sunny and 68°! Don't forget sunscreen and extra water." },
                    Timestamp = now.AddHours(-10),
                    Status = MessageStatus.Delivered,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "maya", SenderName = "Maya Lin", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "alex", SenderName = "Alex Morgan", IsFromMe = false }
                    }
                }
            },

            "sample_wa_sarah" => new List<Message>
            {
                new Message
                {
                    Id = "sc_msg_1",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "sarah",
                    SenderName = "Sarah Connor",
                    Content = new MessageContent { Text = "Hey! How is the People app coming along?" },
                    Timestamp = now.AddHours(-16),
                    Status = MessageStatus.Read,
                    IsOutgoing = false
                },
                new Message
                {
                    Id = "sc_msg_2",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "It's super smooth now! Emoji reactions sync across devices and the audio waveforms look great." },
                    Timestamp = now.AddHours(-15),
                    Status = MessageStatus.Read,
                    IsOutgoing = true,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "sarah", SenderName = "Sarah Connor", IsFromMe = false }
                    }
                },
                new Message
                {
                    Id = "sc_msg_3",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "sarah",
                    SenderName = "Sarah Connor",
                    Content = new MessageContent { Text = "Let's grab coffee at 3 PM today near the Redmond campus." },
                    Timestamp = now.AddHours(-14),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "☕", SenderId = "sarah", SenderName = "Sarah Connor", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },

            "sample_wa_family" => new List<Message>
            {
                new Message
                {
                    Id = "fam_msg_1",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "mom",
                    SenderName = "Mom",
                    Content = new MessageContent { Text = "Sunday dinner is at 6 PM! Dad is grilling salmon." },
                    Timestamp = now.AddDays(-1).AddHours(-2),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "mom", SenderName = "Mom", IsFromMe = false }
                    }
                },
                new Message
                {
                    Id = "fam_msg_2",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "I'll bring the fresh sourdough and dessert!" },
                    Timestamp = now.AddDays(-1).AddHours(-1),
                    Status = MessageStatus.Read,
                    IsOutgoing = true
                },
                new Message
                {
                    Id = "fam_msg_3",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "dad",
                    SenderName = "Dad",
                    Content = new MessageContent { Text = "Cedar plank salmon dinner at 6 PM! 🐟" },
                    Timestamp = now.AddDays(-1),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "❤️", SenderId = "mom", SenderName = "Mom", IsFromMe = false },
                        new MessageReaction { Emoji = "😋", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },

            "sample_wa_alex" => new List<Message>
            {
                new Message
                {
                    Id = "alex_msg_1",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "alex",
                    SenderName = "Alex Morgan",
                    Content = new MessageContent { Text = "Hey, did you manage to get tickets for Sunday's match?" },
                    Timestamp = now.AddDays(-2).AddHours(-2),
                    Status = MessageStatus.Read,
                    IsOutgoing = false
                },
                new Message
                {
                    Id = "alex_msg_2",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "Yes, section 104 row F! Right next to midfield." },
                    Timestamp = now.AddDays(-2).AddHours(-1),
                    Status = MessageStatus.Read,
                    IsOutgoing = true,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "🔥", SenderId = "alex", SenderName = "Alex Morgan", IsFromMe = false }
                    }
                },
                new Message
                {
                    Id = "alex_msg_3",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "alex",
                    SenderName = "Alex Morgan",
                    Content = new MessageContent { Text = "Got the tickets for Sunday's match! Section 104 row F." },
                    Timestamp = now.AddDays(-2),
                    Status = MessageStatus.Read,
                    IsOutgoing = false,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "🔥", SenderId = "alex", SenderName = "Alex Morgan", IsFromMe = false },
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            },

            _ => new List<Message>
            {
                new Message
                {
                    Id = "gen_msg_1",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "peer",
                    SenderName = "Contact",
                    Content = new MessageContent { Text = "Hello! Windows 11 People app testing channel." },
                    Timestamp = now.AddMinutes(-10),
                    Status = MessageStatus.Read,
                    IsOutgoing = false
                },
                new Message
                {
                    Id = "gen_msg_2",
                    ChatId = chatId,
                    PlatformId = "WhatsApp",
                    SenderId = "me",
                    SenderName = "You",
                    Content = new MessageContent { Text = "Message received loud and clear with Fluent Design!" },
                    Timestamp = now.AddMinutes(-5),
                    Status = MessageStatus.Delivered,
                    IsOutgoing = true,
                    Reactions = new List<MessageReaction>
                    {
                        new MessageReaction { Emoji = "👍", SenderId = "me", SenderName = "You", IsFromMe = true }
                    }
                }
            }
        };
    }

    public static Message? FindTestingMessage(string messageId)
    {
        var chats = GetTestingChats();
        foreach (var chat in chats)
        {
            var msgs = GetTestingMessages(chat.Id);
            var match = msgs.FirstOrDefault(m => m.Id == messageId);
            if (match != null) return match;
        }
        return null;
    }

    public static IReadOnlyList<Contact> GetTestingContacts()
    {
        return new List<Contact>
        {
            new Contact { Id = "pavan", Name = "Pavan Davuluri", PhoneNumber = "+1 (425) 555-0101" },
            new Contact { Id = "satya", Name = "Satya Nadella", PhoneNumber = "+1 (425) 555-0102" },
            new Contact { Id = "kevin", Name = "Kevin Gallo", PhoneNumber = "+1 (425) 555-0103" },
            new Contact { Id = "mikhail", Name = "Mikhail Parakhin", PhoneNumber = "+1 (425) 555-0104" },
            new Contact { Id = "dave", Name = "Dave Plummer", PhoneNumber = "+1 (425) 555-0105" },
            new Contact { Id = "zac", Name = "Zac Bowden", PhoneNumber = "+1 (425) 555-0106" },
            new Contact { Id = "joe", Name = "Joe Belfiore", PhoneNumber = "+1 (425) 555-0107" },
            new Contact { Id = "rudy", Name = "Rudy Huyn", PhoneNumber = "+1 (425) 555-0108" },
            new Contact { Id = "jen", Name = "Jen Gentleman", PhoneNumber = "+1 (425) 555-0109" },
            new Contact { Id = "brandon", Name = "Brandon LeBlanc", PhoneNumber = "+1 (425) 555-0110" },
            new Contact { Id = "sarah", Name = "Sarah Connor", PhoneNumber = "+1 (425) 555-0111" },
            new Contact { Id = "alex", Name = "Alex Morgan", PhoneNumber = "+1 (425) 555-0112" },
            new Contact { Id = "maya", Name = "Maya Lin", PhoneNumber = "+1 (425) 555-0113" },
            new Contact { Id = "mom", Name = "Mom", PhoneNumber = "+1 (425) 555-0114" },
            new Contact { Id = "dad", Name = "Dad", PhoneNumber = "+1 (425) 555-0115" }
        };
    }
}
