using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;

namespace AniListImgurPurgeScanner.Model
{
	public enum ActivityType
	{
		List = 0, // Media list activity.
		Text = 1, // Status activity.
		Message = 2, // Message received on the user's profile.

		SentMessage = 3, // Message sent to another profile.

		Count,
	}


	public interface IActivitiesPageData
	{
		IActivitiesPage PageData { get; }
	}

	public interface IActivitiesPage
	{
		IReadOnlyList<IActivity> Activities { get; }
		PageInfo PageInfo { get; set; }
		bool HasNextPage { get; }
		string Error { get; set; }
	}

	public interface IActivity
	{
		string Title { get; }
		string Text { get; }
		AniListUser User { get; }
		string SiteUrl { get; set; }
		long Id { get; set; }
		long CreatedAt { get; set; }
		DateTime CreatedTime { get; }
		List<ActivityReply> Replies { get; set; }
		int ImgurCount { get; }
	}


	public class ActivitiesPageData<TActivity> : IActivitiesPageData where TActivity : IActivity
	{
		[JsonProperty("Page")]
		public ActivitiesPage<TActivity> PageData { get; set; }

		IActivitiesPage IActivitiesPageData.PageData => PageData;
	}

	public class ActivitiesPage<TActivity> : IActivitiesPage where TActivity : IActivity
	{
		[JsonProperty("activities")]
		public List<TActivity> Activities { get; set; }

		// Why does C# not allow an implicit cast here???
		IReadOnlyList<IActivity> IActivitiesPage.Activities => (IReadOnlyList<IActivity>)Activities;

		[JsonProperty("pageInfo")]
		public PageInfo PageInfo { get; set; }

		[JsonIgnore]
		public bool HasNextPage => PageInfo?.HasNextPage ?? false;

		[JsonIgnore]
		public string Error { get; set; }
	}

	public abstract class BaseActivity : IActivity
	{
		// Display name for the activity.
		[JsonIgnore]
		public abstract string Title { get; }

		// Json differs between activity types.
		public abstract string Text { get; set; }

		// Json differs between activity types.
		public abstract AniListUser User { get; set; }

		[JsonIgnore]
		public long UserId => User?.Id ?? 0;

		[JsonIgnore]
		public string UserName => User?.Name;

		[JsonProperty("siteUrl")]
		public string SiteUrl { get; set; }

		[JsonProperty("id")]
		public long Id { get; set; }

		[JsonProperty("createdAt")]
		public long CreatedAt { get; set; }

		[JsonIgnore]
		public DateTime CreatedTime => DateTimeOffset.FromUnixTimeSeconds(CreatedAt).LocalDateTime;

		[JsonProperty("replies")]
		public List<ActivityReply> Replies { get; set; }

		// This assumes the activity text has already been added as an 'IsPost' reply.
		[JsonIgnore]
		public int ImgurCount => Replies.Sum(r => r.ImgurCount);

		public abstract bool IsSent { get; set; }
	}


	public class ListActivity : BaseActivity
	{
		[JsonIgnore]
		public override bool IsSent
		{
			get => false;
			set { }
		}

		//[JsonIgnore]
		public override string Title => Media?.Title?.Romaji;

		[JsonIgnore]
		public override string Text
		{
			get => null;
			set { }
		}

		[JsonProperty("user")]
		public override AniListUser User { get; set; }

		[JsonProperty("media")]
		public ListMedia Media { get; set; }
	}

	public class TextActivity : BaseActivity
	{
		[JsonIgnore]
		public override bool IsSent
		{
			get => false;
			set { }
		}

		//[JsonIgnore]
		public override string Title => "TEXT";

		[JsonProperty("user")]
		public override AniListUser User { get; set; }

		[JsonProperty("text")]
		public override string Text { get; set; }
	}

	public class MessageActivity : BaseActivity
	{
		// True if this is a message sent by the user to another profile.
		// '_' for property that's not part of the AniList API.
		[JsonProperty("_is_sent_", DefaultValueHandling = DefaultValueHandling.Ignore)]
		public override bool IsSent { get; set; }

		//[JsonIgnore]
		public override string Title
		{
			get
			{
				//return (UserName != null ? $"@{UserName}" : "[Removed]");

				// Display as "From @User" or "To @Recipient".
				string name = (!IsSent ? UserName : RecipientName);
				name = (name != null ? $"@{name}" : "[Removed]");
				return (!IsSent ? $"From {name}" : $"To {name}");
			}
		}

		[JsonProperty("messenger")]
		public override AniListUser User { get; set; }

		[JsonProperty("recipient")]
		public AniListUser Recipient { get; set; }

		[JsonIgnore]
		public long RecipientId => Recipient?.Id ?? 0;

		[JsonIgnore]
		public string RecipientName => Recipient?.Name;

		[JsonProperty("message")]
		public override string Text { get; set; }
	}


	public class ActivityReply
	{
		// True if this is the body of a message or text activity.
		// '_' for property that's not part of the AniList API.
		[JsonProperty("_is_post_", DefaultValueHandling = DefaultValueHandling.Ignore)]
		public bool IsPost { get; set; }

		[JsonProperty("user")]
		public AniListUser User { get; set; }

		[JsonIgnore]
		public long UserId => User?.Id ?? 0;

		[JsonIgnore]
		public string Name => User?.Name;

		[JsonProperty("text")]
		public string Text { get; set; }

		[JsonProperty("createdAt")]
		public long CreatedAt { get; set; }

		[JsonIgnore]
		public DateTime CreatedTime => DateTimeOffset.FromUnixTimeSeconds(CreatedAt).LocalDateTime;

		[JsonIgnore]
		public int ImgurCount
		{
			get
			{
				int count = 0;
				int index = Text.IndexOf("imgur", StringComparison.InvariantCultureIgnoreCase);
				while (index != -1)
				{
					count++;
					index = Text.IndexOf("imgur", index + "imgur".Length, StringComparison.InvariantCultureIgnoreCase);
				}
				return count;
			}
		}
	}
}
