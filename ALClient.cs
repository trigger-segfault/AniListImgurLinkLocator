using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AniListImgurPurgeScanner.Model;

using Newtonsoft.Json;

namespace AniListImgurPurgeScanner
{
	public class ALClient
	{
        private bool rateLimited = false;
        private DateTime rateLimitEnds;

		private readonly HttpClient httpClient = new HttpClient();


        private static readonly Dictionary<ActivityType, string> PagesCacheFileNames = new Dictionary<ActivityType, string>
        {
            { ActivityType.List, "list_activities.json" },
            { ActivityType.Text, "text_activities.json" },
            { ActivityType.Message, "message_activities.json" },
        };



        public IReadOnlyList<IActivitiesPage> LoadPagesCache(ActivityType type)
		{
            string fileName = PagesCacheFileNames[type];

            if (File.Exists(fileName))
            {
                var serializer = new JsonSerializer();
                using var stringReader = new StringReader(File.ReadAllText(fileName));
                using var jsonReader = new JsonTextReader(stringReader);

                IReadOnlyList<IActivitiesPage> result = null;
                switch (type)
				{
                case ActivityType.List: result = serializer.Deserialize<List<ActivitiesPage<ListActivity>>>(jsonReader); break;
                case ActivityType.Text: result = serializer.Deserialize<List<ActivitiesPage<TextActivity>>>(jsonReader); break;
                case ActivityType.Message: result = serializer.Deserialize<List<ActivitiesPage<MessageActivity>>>(jsonReader); break;
                }
                return result;
            }
            return null;
        }

        public void SavePagesCache(ActivityType type, IReadOnlyList<IActivitiesPage> pages)
        {
            string fileName = PagesCacheFileNames[type];

            var sb = new StringBuilder();
            using var textWriter = new StringWriter(sb);
            var serializer = new JsonSerializer();
            serializer.Serialize(textWriter, pages);
            File.WriteAllText(fileName, sb.ToString());
        }

        public async Task<IActivitiesPage> LookupPage(ActivityType type, long userId, int page, Action<bool> rateLimitedCallback, CancellationToken cancellationToken)
		{
            switch (type)
			{
            case ActivityType.List: return await LookupPageType<ListActivity>(type, userId, page, rateLimitedCallback, cancellationToken);
            case ActivityType.Text: return await LookupPageType<TextActivity>(type, userId, page, rateLimitedCallback, cancellationToken);
            case ActivityType.Message: return await LookupPageType<MessageActivity>(type, userId, page, rateLimitedCallback, cancellationToken);
            }
            return null;
		}

        private async Task<ActivitiesPage<T>> LookupPageType<T>(ActivityType type, long userId, int page, Action<bool> rateLimitedCallback, CancellationToken cancellationToken) where T : IActivity
        {
            string typeName = null;
            bool hasReplies = false;
            string onQuery = null;
            switch (type)
			{
            case ActivityType.List:
                typeName = "MEDIA_LIST";
                hasReplies = true;
                onQuery = "... on ListActivity { siteUrl id createdAt media { title { romaji }} user { id name } replies { user { id name } createdAt text } } ";
                break;
            case ActivityType.Text:
                typeName = "TEXT";
                hasReplies = false; // Text activities themselves may need to be edited.
                onQuery = "... on TextActivity { siteUrl id createdAt text user { id name } replies { user { id name } createdAt text } } ";
                break;
            case ActivityType.Message:
                typeName = "MESSAGE";
                hasReplies = true;
                onQuery = "... on MessageActivity { siteUrl id createdAt message messenger { id name } replies { user { id name } createdAt text } } ";
                break;
            }

            var result = await CallGraphQLAsync<ActivitiesPageData<T>>(
                "query ($page:Int!, $userId:Int!, $typeName:ActivityType!, $hasReplies:Boolean!) { " +
                    "Page(page:$page, perPage:50) { " +
                        "activities(userId:$userId, type:$typeName, hasReplies:$hasReplies, sort:ID_DESC) { " +
                            onQuery +
                        "} " +
                        "pageInfo { hasNextPage } " +
                    "}" +
                "}",
                new
                {
                    page = page,
                    userId = userId,
                    typeName = typeName,
                    hasReplies = hasReplies,
                },
                rateLimitedCallback,
                cancellationToken);

            var data = result.Data?.PageData;

            if (result.Errors?.Count > 0)
            {
                if (data != null)
                {
                    data.Error = string.Join("\n", result.Errors.Select(x => $"  - {x.Message}"));
                }
                else
                {
                    Console.WriteLine($"GraphQL returned errors:\n{string.Join("\n", result.Errors.Select(x => $"  - {x.Message}"))}");
                    return null;
                }
            }

            if (result.Data != null)
            {
                // Filter activities with imgur links.
                for (int i = 0; i < data.Activities.Count; i++)
                {
                    var activity = data.Activities[i];

                    // Add a 'reply' for the post body when applicable.
                    // Skip adding messages since we're not editing other users' links.
                    if (type == ActivityType.Text /*|| type == ActivityType.Message*/)
					{
                        var body = new ActivityReply
                        {
                            User = new AniListUser
                            {
                                Id = activity.User.Id,
                                Name = activity.User.Name,
                            },
                            Text = activity.Text,
                            IsPost = true, // Not a real reply
                        };
                        activity.Replies.Insert(0, body);
                    }

                    // Filter replies with imgur links.
                    for (int j = 0; j < activity.Replies.Count; j++)
                    {
                        var reply = activity.Replies[j];
                        if (reply.UserId != userId || reply.ImgurCount == 0)
                        {
                            activity.Replies.RemoveAt(j);
                            j--;
                        }
                    }

                    // Check if this post had no imgur links and if so, remove it.
                    if (activity.Replies.Count == 0)
                    {
                        data.Activities.RemoveAt(i);
                        i--;
                    }
                }
            }

            return result.Data.PageData;
        }

        public async Task<AniListUser> LookupUser(string userName, Action<bool> rateLimitedCallback, CancellationToken cancellationToken)
		{
            // Call GraphQL endpoint here, specifying return data type, endpoint, method, query, and variables
            var result = await CallGraphQLAsync<AniListUserLookup>(
                "query ($userName: String!) { " +
                    "User(name:$userName) { id name }" +
                "}",
                new
                {
                    userName = userName,
                },
                rateLimitedCallback,
                cancellationToken);

            // Examine the GraphQL response to see if any errors were encountered
            if (result.Errors?.Count > 0)
            {
                //if (result.Data?.PageData != null)
                //{
                //    result.Data.PageData.Error = string.Join("\n", result.Errors.Select(x => $"  - {x.Message}"));
                //}
                //else
                //{
                    Console.WriteLine($"GraphQL returned errors:\n{string.Join("\n", result.Errors.Select(x => $"  - {x.Message}"))}");
                    return null;
                //}
            }

            return result.Data.User;
        }

        private async Task WaitForRateLimit(Action<bool> rateLimitedCallback)
        {
            if (rateLimited)
            {
                DateTime now = DateTime.UtcNow;
                if (now < rateLimitEnds)
                {
                    rateLimitedCallback?.Invoke(true);

                    TimeSpan delay = rateLimitEnds.Subtract(now);
                    await Task.Delay(delay);
                }
                rateLimited = false;
                rateLimitedCallback?.Invoke(false);
            }
            else
			{
                // Generic delay between requests to avoid Cloudflare burst rate-limiting.
                await Task.Delay(50);
			}
        }

        private async Task<GraphQLResponse<TResponse>> CallGraphQLAsync<TResponse>(string query, object variables, Action<bool> rateLimitedCallback, CancellationToken cancellationToken)
        {
            GraphQLResponse<TResponse> graphQLResponse = null;
            bool nextRateLimited = false;

            // Loop if we get rate-limited during the request, and wait till the rate-limit ends.
            do
            {
                // If we know we're currently rate-limited, then wait for it to end.
                await WaitForRateLimit(rateLimitedCallback);

                var content = new StringContent(SerializeGraphQLCall(query, variables), Encoding.UTF8, "application/json");
                var httpRequestMessage = new HttpRequestMessage
                {
                    RequestUri = new Uri("https://graphql.anilist.co"),
                    Method = HttpMethod.Post,
                    Content = content,
                };
                httpRequestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                //add authorization headers if necessary here
                /*if (authorization != null)
                {
                    httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorization);
                }*/

                using (var response = await httpClient.SendAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false))
                {
                    //int rateLimitLimit = -1;
                    //long rateLimitReset = -1;
                    //int rateLimitRemaining = -1;
                    IEnumerable<string> values;
                    /*if (response.Headers.TryGetValues("X-RateLimit-Limit", out values))
                    {
                        rateLimitLimit = int.Parse(values.First());
                    }
                    if (response.Headers.TryGetValues("X-RateLimit-Reset", out values))
                    {
                        rateLimitReset = long.Parse(values.First());
                    }*/
                    if (response.Headers.RetryAfter?.Delta != null)
                    {
                        // We've already hit the rate limit, make sure to wait and then retry the request.
                        rateLimitEnds = DateTime.UtcNow.Add(response.Headers.RetryAfter.Delta.Value);
                        rateLimitEnds.AddSeconds(2); // 2 Extra seconds to avoid getting too close to rate limit
                        rateLimited = true;
                    }
                    /*if (response.Headers.TryGetValues("Retry-After", out values))
                    {
                        // We've already hit the rate limit, make sure to wait and then retry the request.
                        int rateLimitRetryAfter = int.Parse(values.First());
                        rateLimitEnds = DateTime.UtcNow.AddSeconds(rateLimitRetryAfter);
                        rateLimited = true;
                    }*/
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        // Avoid CloudFlare burst rate limit.
                        rateLimitEnds = DateTime.UtcNow.AddSeconds(60);
                        rateLimitEnds.AddSeconds(2); // 2 Extra seconds to avoid getting too close to rate limit
                        rateLimited = true;
                    }
                    else if (response.Headers.TryGetValues("X-RateLimit-Remaining", out values))
                    {
                        int rateLimitRemaining = int.Parse(values.First());
                        if (rateLimitRemaining <= 0)
                        {
                            // We'll hit the rate limit next request, so pre-emptively setup rate limit wait.
                            nextRateLimited = true;
                            rateLimitEnds = DateTime.UtcNow.AddSeconds(60);
                            rateLimitEnds.AddSeconds(2); // 2 Extra seconds to avoid getting too close to rate limit
                        }
                    }

                    if (!rateLimited)
                    {
                        if (response?.Content.Headers.ContentType?.MediaType == "application/json")
                        {
                            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false); //cancellationToken supported for .NET 5/6
                            graphQLResponse = DeserializeGraphQLCall<TResponse>(responseString);
                        }
                        else
                        {
                            throw new ApplicationException($"Unable to contact 'https://graphql.anilist.co': {response.StatusCode} - {response.ReasonPhrase}");
                        }
                    }
                }
            }
            while (rateLimited);

            rateLimited = nextRateLimited;

            return graphQLResponse;
        }

        public class GraphQLErrorLocation
        {
            public int Line { get; set; }
            public int Column { get; set; }
        }

        public class GraphQLError
        {
            public string Message { get; set; }
            public List<GraphQLErrorLocation> Locations { get; set; }
            public List<object> Path { get; set; } //either int or string
        }

        public class GraphQLResponse<TResponse>
        {
            public List<GraphQLError> Errors { get; set; }
            public TResponse Data { get; set; }
        }

        private static string SerializeGraphQLCall(string query, object variables)
        {
            var sb = new StringBuilder();
            using var textWriter = new StringWriter(sb);
            var serializer = new JsonSerializer();
            serializer.Serialize(textWriter, new
            {
                query = query,
                variables = variables,
            });
            return sb.ToString();
        }

        private static GraphQLResponse<TResponse> DeserializeGraphQLCall<TResponse>(string response)
        {
            var serializer = new JsonSerializer();
            using var stringReader = new StringReader(response);
            using var jsonReader = new JsonTextReader(stringReader);
            var result = serializer.Deserialize<GraphQLResponse<TResponse>>(jsonReader);
            return result;
        }
    }
}
