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

	public string BotUsername { get; set; } = "";

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
