using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace VyralBot.UI.ViewModels;

public class ChatMessage : INotifyPropertyChanged
{
	private static readonly ConcurrentDictionary<string, BitmapImage> _badgeImageCache;

	internal static ILogger? _logger;

	private Dictionary<string, string> _badges = new Dictionary<string, string>();

	private List<BadgeInfo>? _badgeImages;

	private static readonly HttpClient _httpClient;

	public string Platform { get; set; } = "";

	public string Username { get; set; } = "";

	public string Message { get; set; } = "";

	public DateTime Timestamp { get; set; }

	/// <summary>Time for today's messages; date + time for older history.</summary>
	public string TimestampDisplay => (Timestamp.Date == DateTime.Now.Date)
		? Timestamp.ToString("h:mm tt")
		: Timestamp.ToString("MMM d, h:mm tt");

	public string Color { get; set; } = "#FFFFFF";

	public bool IsModerator { get; set; }

	public bool IsSubscriber { get; set; }

	public bool IsVip { get; set; }

	public bool IsBroadcaster { get; set; }

	public bool IsSystemMessage { get; set; } = false;

	public Dictionary<string, string> Badges
	{
		get
		{
			return _badges;
		}
		set
		{
			_logger?.LogInformation("[Badges setter] Setting {Count} badges: {BadgeList}", value.Count, string.Join(", ", value.Select<KeyValuePair<string, string>, string>((KeyValuePair<string, string> kvp) => kvp.Key)));
			_badges = value;
			_badgeImages = null;
			BadgeImages = ComputeBadgeImages();
			_logger?.LogInformation("[Badges setter] Computed {Count} badge images", BadgeImages.Count);
		}
	}

	public List<BadgeInfo> BadgeImages
	{
		get
		{
			return _badgeImages ?? new List<BadgeInfo>();
		}
		private set
		{
			_badgeImages = value;
			_logger?.LogInformation("[BadgeImages setter] Setting {Count} images. First image details: Width={Width}, Height={Height}", value.Count, (value.Count > 0) ? value[0].Image.PixelWidth : 0, (value.Count > 0) ? value[0].Image.PixelHeight : 0);
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("BadgeImages"));
			_logger?.LogInformation("[BadgeImages setter] PropertyChanged fired for BadgeImages");
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public void InitializeBadges()
	{
		_logger?.LogInformation("[InitializeBadges] Called with {Count} badges, _badgeImages is {State}", _badges.Count, (_badgeImages == null) ? "null" : "not null");
		if (_badges.Count > 0)
		{
			_logger?.LogInformation("[InitializeBadges] Badges: {Badges}", string.Join(", ", _badges.Select<KeyValuePair<string, string>, string>((KeyValuePair<string, string> b) => b.Key + "=" + b.Value)));
		}
		if (_badgeImages == null && _badges.Count > 0)
		{
			_logger?.LogInformation("[InitializeBadges] Computing badge images...");
			BadgeImages = ComputeBadgeImages();
			_logger?.LogInformation("[InitializeBadges] Computed {Count} images", BadgeImages.Count);
		}
	}

	static ChatMessage()
	{
		_badgeImageCache = new ConcurrentDictionary<string, BitmapImage>();
		_httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(5L)
		};
		_httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
		_httpClient.DefaultRequestHeaders.Add("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
		_httpClient.DefaultRequestHeaders.Add("Referer", "https://kick.com/");
		_httpClient.DefaultRequestHeaders.Add("Origin", "https://kick.com");
	}

	private static BitmapImage LoadBadgeImage(string url)
	{
		return _badgeImageCache.GetOrAdd(url, delegate(string uri)
		{
			try
			{
				byte[] array;
				if (uri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
				{
					_logger?.LogInformation("[LoadBadgeImage] Loading embedded data URI");
					string s = uri.Substring(uri.IndexOf(",") + 1);
					array = Convert.FromBase64String(s);
					_logger?.LogInformation("[LoadBadgeImage] Decoded {Size} bytes from base64", array.Length);
				}
				else
				{
					_logger?.LogInformation("[LoadBadgeImage] Downloading: {Uri}", uri);
					array = _httpClient.GetByteArrayAsync(uri).GetAwaiter().GetResult();
					_logger?.LogInformation("[LoadBadgeImage] Downloaded {Size} bytes", array.Length);
				}
				MemoryStream memoryStream = new MemoryStream(array);
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.StreamSource = memoryStream;
				bitmapImage.EndInit();
				memoryStream.Dispose();
				((Freezable)bitmapImage).Freeze();
				_logger?.LogInformation("[LoadBadgeImage] ✅ Successfully created BitmapImage: Width={Width}, Height={Height}, PixelWidth={PixelWidth}, PixelHeight={PixelHeight}, IsFrozen={IsFrozen}", bitmapImage.Width, bitmapImage.Height, bitmapImage.PixelWidth, bitmapImage.PixelHeight, ((Freezable)bitmapImage).IsFrozen);
				return bitmapImage;
			}
			catch (Exception exception)
			{
				_logger?.LogError(exception, "[LoadBadgeImage] ❌ ERROR loading badge from {Uri}", uri);
				return (BitmapImage)null;
			}
		});
	}

	private List<BadgeInfo> ComputeBadgeImages()
	{
		_logger?.LogInformation("[ComputeBadgeImages] Starting with {Count} badges", Badges.Count);
		List<BadgeInfo> list = new List<BadgeInfo>();
		foreach (KeyValuePair<string, string> item in Badges.OrderBy<KeyValuePair<string, string>, int>((KeyValuePair<string, string> b) => GetBadgePriority(b.Key)))
		{
			_logger?.LogInformation("[ComputeBadgeImages] Processing badge: {Key}={Value}", item.Key, item.Value);
			string text = null;
			string name = item.Key;
			switch (item.Key.ToLowerInvariant())
			{
			case "broadcaster":
				text = "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/1";
				name = "Broadcaster";
				break;
			case "moderator":
				text = "https://static-cdn.jtvnw.net/badges/v1/3267646d-33f0-4b17-b3df-f923a41db1d0/1";
				name = "Moderator";
				break;
			case "vip":
				text = "https://static-cdn.jtvnw.net/badges/v1/b817aba4-fad8-49e2-b88a-7cc744dfa6ec/1";
				name = "VIP";
				break;
			case "subscriber":
				text = "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/1";
				name = "Subscriber";
				break;
			case "verified":
				text = "https://static-cdn.jtvnw.net/badges/v1/d12a2e27-16f6-41d0-ab77-b780518f00a3/1";
				name = "Verified";
				break;
			}
			if (text != null)
			{
				_logger?.LogInformation("[ComputeBadgeImages] Loading image from: {Url}", text);
				BitmapImage bitmapImage = LoadBadgeImage(text);
				if (bitmapImage != null)
				{
					_logger?.LogInformation("[ComputeBadgeImages] ✅ Image loaded successfully");
					list.Add(new BadgeInfo
					{
						Name = name,
						Image = bitmapImage
					});
				}
				else
				{
					_logger?.LogInformation("[ComputeBadgeImages] ❌ Image load returned null");
				}
			}
			else
			{
				_logger?.LogInformation("[ComputeBadgeImages] ⚠\ufe0f No URL mapping for badge: {Badge}", item.Key);
			}
		}
		_logger?.LogInformation("[ComputeBadgeImages] Returning {Count} images", list.Count);
		return list;
	}

	private static int GetBadgePriority(string badgeType)
	{
		string text = badgeType.ToLowerInvariant();
		if (1 == 0)
		{
		}
		int result = text switch
		{
			"broadcaster" => 0, 
			"moderator" => 1, 
			"vip" => 2, 
			"subscriber" => 3, 
			"verified" => 4, 
			_ => 99, 
		};
		if (1 == 0)
		{
		}
		return result;
	}
}
