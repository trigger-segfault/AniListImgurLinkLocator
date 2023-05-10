using Newtonsoft.Json;

using System;

namespace AniListImgurPurgeScanner.Model
{
	public class AniListUser
	{
		[JsonProperty("id")]
		public long Id { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }
	}

	public class AniListUserLookup
	{
		[JsonProperty("User")]
		public AniListUser User { get; set; }
	}

	public class PageInfo
	{
		[JsonProperty("hasNextPage")]
		public bool HasNextPage { get; set; }
	}

	public class ListMedia
	{
		[JsonProperty("title")]
		public ListMediaTitle Title { get; set; }
	}

	public class ListMediaTitle
	{
		[JsonProperty("romaji")]
		public string Romaji { get; set; }
	}
}
