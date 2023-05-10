using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AniListImgurPurgeScanner.Model
{
	public class Settings
	{
		private const string SettingsFileName = "settings.json";


		[JsonProperty("currentType")]
		public ActivityType CurrentType { get; set; } = ActivityType.List;

		[JsonProperty("currentActivityId")]
		public long CurrentActivityId { get; set; } = 0;

		[JsonProperty("autoOpenUrl")]
		public bool AutoOpenUrl { get; set; } = true;

		[JsonProperty("userName")]
		public string UserName { get; set; } = null;

		[JsonProperty("userId")]
		public long UserId { get; set; } = 0;


		public bool Validate()
		{
			bool hasErrors = false;

			if (CurrentType < 0 || CurrentType >= ActivityType.Count)
			{
				CurrentType = ActivityType.List;
				hasErrors = true;
			}

			return hasErrors;
		}

		public bool Load()
		{
			if (File.Exists(SettingsFileName))
			{
				var serializer = new JsonSerializer();
				using var stringReader = new StringReader(File.ReadAllText(SettingsFileName));
				using var jsonReader = new JsonTextReader(stringReader);
				serializer.Populate(jsonReader, this); // Doesn't work...

				return true;
			}
			return false;
		}

		public void Save()
		{
			var sb = new StringBuilder();
			using var textWriter = new StringWriter(sb);
			var serializer = new JsonSerializer();
			serializer.Serialize(textWriter, this);
			File.WriteAllText(SettingsFileName, sb.ToString());
		}
	}
}
