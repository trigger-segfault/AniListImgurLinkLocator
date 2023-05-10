using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using AniListImgurPurgeScanner.Extensions;
using AniListImgurPurgeScanner.Model;

namespace AniListImgurPurgeScanner
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{

		private readonly List<IActivitiesPage> pages = new List<IActivitiesPage>();
		private IActivitiesPage currentPage;
		private IActivity currentActivity;

		private readonly Settings settings = new Settings();
		private readonly ALClient al = new ALClient();

		private bool loading = true;
		private bool working = false;



		public MainWindow()
		{
			InitializeComponent();
		}

		private void OnWindowLoaded(object sender, RoutedEventArgs e)
		{
			settings.Load();
			settings.Validate();

			loading = true;
			comboBoxActivityType.SelectedIndex = (int)settings.CurrentType;
			textBoxUserName.Text = settings.UserName ?? "";
			checkBoxAutoOpen.IsChecked = settings.AutoOpenUrl;
			loading = false;

			if (settings.CurrentActivityId != 0)
			{
				textBoxActivityId.Text = settings.CurrentActivityId.ToString();
			}
		}

		private void OnActivityTypeChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!loading)
			{
				settings.CurrentType = (ActivityType)comboBoxActivityType.SelectedIndex;
				settings.Save();
			}
		}

		private async void OnButtonLoadClick(object sender, RoutedEventArgs e)
		{
			if (working)
			{
				return;
			}

			buttonLoad.Content = "Loading...";
			resultText.Content = "Working...";
			countText.Content = "Count";

			string result = await DoLoadPages(false);

			if (pages.Count > 0 && pages[0].Activities.Count > 0)
			{
				IActivity targetActivity = FindActivityById(settings.CurrentActivityId);
				if (targetActivity == null)
				{
					targetActivity = pages[0].Activities[0];
				}
				ChangeActivity(targetActivity);
			}

			if (result != null)
			{
				resultText.Content = result;
				buttonLoad.Content = "Loaded";
				progressText.Content = "Done";
			}
			else
			{
				buttonLoad.Content = "Load";
				progressText.Content = "Failed";
			}
		}

		private async void OnButtonReloadClick(object sender, RoutedEventArgs e)
		{
			if (working)
			{
				return;
			}

			buttonReload.Content = "Reloading...";
			resultText.Content = "Working...";
			countText.Content = "Count";

			string result = await DoLoadPages(true);

			if (pages.Count > 0 && pages[0].Activities.Count > 0)
			{
				IActivity targetActivity = FindActivityById(settings.CurrentActivityId);
				if (targetActivity == null)
				{
					targetActivity = pages[0].Activities[0];
				}
				ChangeActivity(targetActivity);
			}

			if (result != null)
			{
				resultText.Content = result;
				buttonReload.Content = "Reloaded";
				progressText.Content = "Done";
			}
			else
			{
				buttonReload.Content = "Reload";
				progressText.Content = "Failed";
			}
		}

		private void CountRemaining(object sender = null, RoutedEventArgs e = null)
		{
			int activitiesLeft = 0;
			int repliesLeft = 0;
			int imgurLeft = 0;
			int pageIndex = pages.IndexOf(currentPage);
			int activityIndex = currentPage.Activities.IndexOf(currentActivity);
			for (int i = pageIndex; i < pages.Count; i++)
			{
				int j = (i==pageIndex ? activityIndex : 0);
				for (; j < pages[i].Activities.Count; j++)
				{
					activitiesLeft++;
					repliesLeft += pages[i].Activities[j].Replies.Count;
					imgurLeft += pages[i].Activities[j].ImgurCount;
				}
			}
			countText.Content = $"Remaining Activities: {activitiesLeft}, Replies: {repliesLeft}, Imgur Links: {imgurLeft}";
		}

		private void CopyActivityId(object sender = null, RoutedEventArgs e = null)
		{
			if (currentActivity != null)
			{
				Clipboard.SetText(currentActivity.Id.ToString());
			}
		}

		private void CopyActivityUrl(object sender = null, RoutedEventArgs e = null)
		{
			if (currentActivity?.SiteUrl != null)
			{
				Clipboard.SetText(currentActivity.SiteUrl);
			}
		}

		private void OpenActivityUrl(object sender = null, RoutedEventArgs e = null)
		{
			if (currentActivity?.SiteUrl != null)
			{
				///TODO: Is this leaking anything? Do Processes need to be disposed of?
				Process.Start(new ProcessStartInfo
				{
					FileName = currentActivity.SiteUrl,
					UseShellExecute = true,
				});
			}
		}

		private bool ChangeActivity(IActivity activity)
		{
			if (activity != null)
			{
				foreach (var page in pages)
				{
					if (page.Activities.Contains(activity))
					{
						currentPage = page;
						break;
					}
				}
				currentActivity = activity;
				textBoxActivityId.Text = activity.Id.ToString();
				statusBarName.Text = activity.Title ?? "-";
				statusBarDate.Text = activity.CreatedTime.ToString();

				// Id of 0 is only used for dummy' replies that make it easier to manage text activity posts.
				int posts = 0;
				if (currentActivity.Replies.Count > 0 && currentActivity.Replies[0].IsPost)
				{
					posts = 1;
				}
				statusBarPosts.Text = posts.ToString();
				statusBarReplies.Text = (currentActivity.Replies.Count - posts).ToString();
				statusBarImgurLinks.Text = currentActivity.ImgurCount.ToString();

				settings.CurrentActivityId = currentActivity.Id;
				settings.Save();
				return true;
			}
			else
			{
				textBoxActivityId.Text = "";
				statusBarName.Text = "-";
				statusBarDate.Text = "";
				currentPage = null;
				currentActivity = null;

				statusBarPosts.Text = "-";
				statusBarReplies.Text = "-";
				statusBarImgurLinks.Text = "-";
				return false;
			}
		}

		private void OnRateLimited(bool rateLimitActive)
		{
			Dispatcher.Invoke(() => {
				if (rateLimitActive)
				{
					resultText.Content = "Waiting for rate limit to end...";
				}
				else
				{
					resultText.Content = "Working...";
				}
			});
		}

		private async Task<string> DoLoadPages(bool reload = false, int limit = -1)
		{
			working = true;
			try
			{
				CancellationToken token = new CancellationToken();

				ActivityType type = (ActivityType)comboBoxActivityType.SelectedIndex;

				// Validate the specified username and get the user id if valid.
				if (string.IsNullOrWhiteSpace(settings.UserName))
				{
					MessageBox.Show("Please enter a username first");
					return "No user specified";
				}
				if (settings.UserId == 0)
				{
					var user = await al.LookupUser(settings.UserName, OnRateLimited, token);
					if (user == null)
					{
						MessageBox.Show($"Could not find AniList user '{settings.UserName}'");
						return "Could not find user";
					}
					else
					{
						settings.UserId = user.Id;
						settings.Save();
					}
				}

				pages.Clear();

				IReadOnlyList<IActivitiesPage> cache = null;
				if (!reload)
				{
					cache = al.LoadPagesCache(type);
				}

				int page = 1;
				int pageCount = 0;
				IActivitiesPage data = null;
				int activityCount = 0;
				int replyCount = 0;
				int imgurCount = 0;
				do
				{
					Dispatcher.Invoke(() => {
						progressText.Content = $"Loading Page... {page}";
					});
					if (cache != null)
					{
						data = cache[pageCount];
					}
					else
					{
						data = await al.LookupPage(type, settings.UserId, page++, OnRateLimited, token);
					}
					if (data != null && data.Activities != null && data.Activities.Count > 0)
					{
						activityCount += data.Activities.Count;
						replyCount += data.Activities.Sum(a => a.Replies.Count);
						imgurCount += data.Activities.Sum(a => a.ImgurCount);
						pages.Add(data);
					}
					pageCount++;
				} while (data != null && data.Error == null && data.HasNextPage && (limit == -1 || pageCount < limit));


				if (data != null && data.Error != null)
				{
					return data.Error;
				}
				else
				{
					string cachedString = (cache != null ? " [Cache]" : "");
					if (cache == null)
					{
						al.SavePagesCache(type, pages);
					}
					return $"Pages: {pageCount}, Activities: {activityCount}, Replies: {replyCount}, Imgur Links: {imgurCount}{cachedString}";
				}
				//return null;
			}
			finally
			{
				working = false;
			}
		}

		private void NextActivity(object sender, RoutedEventArgs e)
		{
			int pageIndex = pages.IndexOf(currentPage);
			int activityIndex = currentPage.Activities.IndexOf(currentActivity);
			if (activityIndex + 1 >= currentPage.Activities.Count)
			{
				if (pageIndex + 1 >= pages.Count)
				{
					MessageBox.Show("No more activities with imgur links");
					return;
				}
				else
				{
					ChangeActivity(pages[++pageIndex].Activities[0]);
				}
			}
			else
			{
				ChangeActivity(currentPage.Activities[++activityIndex]);
			}
			if (settings.AutoOpenUrl)
			{
				OpenActivityUrl();
			}
		}

		private void PreviousActivity(object sender, RoutedEventArgs e)
		{
			int pageIndex = pages.IndexOf(currentPage);
			int activityIndex = currentPage.Activities.IndexOf(currentActivity);
			if (activityIndex - 1 < 0)
			{
				if (pageIndex - 1 < 0)
				{
					MessageBox.Show("No more activities with imgur links");
					return;
				}
				else
				{
					ChangeActivity(pages[--pageIndex].Activities.Last());
				}
			}
			else
			{
				ChangeActivity(currentPage.Activities[--activityIndex]);
			}
			if (settings.AutoOpenUrl)
			{
				OpenActivityUrl();
			}
		}

		private IActivity FindActivityById(long id)
		{
			foreach (var page in pages)
			{
				foreach (var activity in page.Activities)
				{
					if (activity.Id == id)
					{
						return activity;
					}
				}
			}
			return null;
		}

		private void GotoActivity(object sender, RoutedEventArgs e)
		{
			if (long.TryParse(textBoxActivityId.Text, out long targetId)) {
				IActivity targetActivity = FindActivityById(targetId);

				if (targetActivity == null)
				{
					MessageBox.Show($"Activity of type '{((ActivityType)comboBoxActivityType.SelectedIndex)}' not found");
					return;
				}
				else
				{
					ChangeActivity(targetActivity);
				}
			}
			if (settings.AutoOpenUrl)
			{
				OpenActivityUrl();
			}
		}

		private void OnUsernameChanged(object sender, TextChangedEventArgs e)
		{
			if (!loading)
			{
				settings.UserName = textBoxUserName.Text;
				settings.UserId = 0;
				settings.Save();
			}
		}

		private void OnAutoOpenChanged(object sender, RoutedEventArgs e)
		{
			if (!loading)
			{
				settings.AutoOpenUrl = checkBoxAutoOpen.IsChecked.GetValueOrDefault(true);
				settings.Save();
			}
		}
	}
}
