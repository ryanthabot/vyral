using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace VyralBot.UI.ViewModels;

public class DmConversation : INotifyPropertyChanged
{
	private string _lastPreview = "";

	private DateTime _lastAt;

	private int _unreadCount;

	public string Username { get; set; } = "";

	public string BotId { get; set; } = "";

	public string ChannelId { get; set; } = "";

	private string? _userId;

	/// <summary>The human participant's Discord snowflake id, when known — used for reliable by-id DM sends and search.</summary>
	public string? UserId
	{
		get
		{
			return _userId;
		}
		set
		{
			if (!string.Equals(_userId, value, StringComparison.Ordinal))
			{
				_userId = value;
				OnPropertyChanged("UserId");
				OnPropertyChanged("HeaderDetail");
			}
		}
	}

	public string BotUsername { get; set; } = "";

	/// <summary>Muted text in the thread header: "(Discord • <id>)" when the user id is known, else the platform name.</summary>
	public string HeaderDetail => string.IsNullOrEmpty(UserId) ? Platform : ("(Discord • " + UserId + ")");

	/// <summary>True once the FULL persisted history has been loaded into Messages.</summary>
	public bool HistoryLoaded { get; set; }

	private string _linkedPlatformsDisplay = "";

	public string LinkedPlatformsDisplay
	{
		get
		{
			return _linkedPlatformsDisplay;
		}
		set
		{
			_linkedPlatformsDisplay = value;
			OnPropertyChanged("LinkedPlatformsDisplay");
		}
	}

	public string Platform { get; set; } = "Discord";

	public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();

	public string LastPreview
	{
		get
		{
			return _lastPreview;
		}
		set
		{
			_lastPreview = value;
			OnPropertyChanged("LastPreview");
		}
	}

	public DateTime LastAt
	{
		get
		{
			return _lastAt;
		}
		set
		{
			_lastAt = value;
			OnPropertyChanged("LastAt");
			OnPropertyChanged("LastAtText");
		}
	}

	public string LastAtText => (LastAt == default(DateTime)) ? "" : ((LastAt.Date == DateTime.Now.Date) ? LastAt.ToString("h:mm tt") : LastAt.ToString("MMM d"));

	public int UnreadCount
	{
		get
		{
			return _unreadCount;
		}
		set
		{
			_unreadCount = value;
			OnPropertyChanged("UnreadCount");
			OnPropertyChanged("HasUnread");
		}
	}

	public bool HasUnread => UnreadCount > 0;

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged(string propertyName)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}

/// <summary>
/// Lightweight DM search hit shown in the inbox while a search query is
/// active: the conversation's username, the matched message snippet (or the
/// conversation preview) and its time. Clicking one opens the conversation.
/// </summary>
public class DmSearchResult
{
	public string Username { get; set; } = "";

	public string? UserId { get; set; }

	public string BotId { get; set; } = "";

	public string ChannelId { get; set; } = "";

	/// <summary>Drives the platform logo style; DM hits are always Discord.</summary>
	public string Platform { get; set; } = "Discord";

	public string Snippet { get; set; } = "";

	public DateTime Timestamp { get; set; }

	public string TimeText => (Timestamp == default(DateTime)) ? "" : ((Timestamp.Date == DateTime.Now.Date) ? Timestamp.ToString("h:mm tt") : Timestamp.ToString("MMM d"));
}
