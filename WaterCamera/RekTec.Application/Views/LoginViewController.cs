#region 文件头部
/**********
Copyright @ 苏州瑞泰信息技术有限公司 All rights reserved. 
****************
作者 :carziertong
日期 : 2015-04-07
说明 : 登录页面
****************/

#endregion
using CoreGraphics;
using Foundation;
using MonoTouch.Dialog;
using RekTec.Contacts.Services;
using RekTec.Corelib.Configuration;
using RekTec.Corelib.Utils;
using RekTec.Corelib.Views;
using RekTec.Messages.PushNotification.Services;
using RekTec.MyProfile.Services;
using RekTec.MyProfile.ViewModels;
using RekTec.MyProfile.Views;
using RekTec.Version.Services;
using RekTec.Application.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using UIKit;
using RekTec.Chat.Service;
using System.Collections.Generic;
using ObjCRuntime;
using System.Diagnostics;
using LocalAuthentication;
using Newtonsoft.Json;

namespace RekTec.Application.Views
{
	/// <summary>
	/// 登录页面
	/// </summary>
	public class LoginViewController : BaseViewController
	{
		private UITableView _tableView;
		UIWebView _webView;
		private UIViewBuilder _builder;
		private NSObject obs1, obs2;
		private nfloat _logoHeight;
		private UIImage _logo = UIImage.FromFile ("login_top.png");

		private List<DropdownListViewItem> dropdownListItem;

		/// <summary>
		/// 页面加载的时候
		/// </summary>
		public override void ViewDidLoad ()
		{
			base.ViewDidLoad ();

			_logoHeight = (View.Bounds.Width / _logo.Size.Width) * _logo.Size.Height;

			_builder = new UIViewBuilder (this.View);
			_tableView = _builder.CreateTableView (View.Bounds);
			_tableView.BackgroundColor = UIColor.White;
			_tableView.SeparatorStyle = UITableViewCellSeparatorStyle.None;
			_tableView.ScrollEnabled = false;

			//修改状态栏背景颜色
			UIView statusview = new UIView (new CGRect (0, 0, View.Frame.Width, UiStyleSetting.StatusBarHeight));
			statusview.BackgroundColor = UiStyleSetting.RektecBlueColor;
			View.AddSubview (statusview);
		}

		/// <summary>
		/// 页面每次出现时执行
		/// </summary>
		public override async void ViewWillAppear (bool animated)
		{
			base.ViewWillAppear (animated);

			this.NavigationController.NavigationBarHidden = true;

			View.AddGestureRecognizer (new TapGestureRecognizer (this, new Selector ("ViewTap")));

			if (!string.IsNullOrWhiteSpace (GlobalAppSetting.UserCode)) {
				AlertUtil.ShowWaiting ("正在获取酒店信息...");
				var hotelInfo = await AuthenticationService.GetHotelInfo (GlobalAppSetting.UserCode);
				if (hotelInfo != null) {
					dropdownListItem = new List<DropdownListViewItem> ();
					foreach (var hotel in hotelInfo.Data.Hotels) {
						dropdownListItem.Add (new DropdownListViewItem {
							Value = hotel.HotelCd,
							Text = hotel.HotelName,
							BrandCd = hotel.BrandCd
						});
					}
				}
				AlertUtil.DismissWaiting ();
			}

			_tableView.Source = new Source (this);
			_tableView.ReloadData ();


			obs1 = NSNotificationCenter.DefaultCenter.AddObserver (UIKeyboard.WillShowNotification, delegate (NSNotification n) {
				var duration = UIKeyboard.AnimationDurationFromNotification (n);

				UIView.BeginAnimations ("ResizeForKeyboard");
				UIView.SetAnimationDuration (duration);
				var contentInsets = new UIEdgeInsets (-(_logoHeight - 15), 0, 0, 0);
				_tableView.ContentInset = contentInsets;
				_tableView.ScrollIndicatorInsets = contentInsets;
				UIView.CommitAnimations ();
			});

			obs2 = NSNotificationCenter.DefaultCenter.AddObserver (UIKeyboard.WillHideNotification, delegate (NSNotification n) {
				var duration = UIKeyboard.AnimationDurationFromNotification (n);
				UIView.BeginAnimations ("ResizeForKeyboard");
				UIView.SetAnimationDuration (duration);
				var contentInsets = new UIEdgeInsets (0, 0, 0, 0);
				_tableView.ContentInset = contentInsets;
				_tableView.ScrollIndicatorInsets = contentInsets;
				UIView.CommitAnimations ();
			});
		}

		/// <summary>
		/// 收起键盘
		/// </summary>
		[Export ("ViewTap")]
		void ViewTap ()
		{
			UIApplication.SharedApplication.KeyWindow.EndEditing (true);
		}

		class TapGestureRecognizer : UITapGestureRecognizer
		{
			public TapGestureRecognizer (NSObject target, Selector action) : base (target, action)
			{
				ShouldReceiveTouch += (sender, touch) => {
					return true;
				};
			}
		}

		public override void ViewDidDisappear (bool animated)
		{
			base.ViewDidDisappear (animated);
			NSNotificationCenter.DefaultCenter.RemoveObserver (obs1);
			NSNotificationCenter.DefaultCenter.RemoveObserver (obs2);
		}

		/// <summary>
		/// 页面将要消失时候执行
		/// </summary>
		/// <param name="animated">If set to <c>true</c> animated.</param>
		public override void ViewWillDisappear (bool animated)
		{
			//this.NavigationController.NavigationBarHidden = false;
		}

		private void PreloadWebResource ()
		{
			//清除缓存
			NSUrlCache.SharedCache.RemoveAllCachedResponses ();

			//设置cookie的接受政策
			NSHttpCookieStorage.SharedStorage.AcceptPolicy = NSHttpCookieAcceptPolicy.Always;

			_webView = new UIWebView (new CGRect (0, 0, 0, 0));

			string address;
			if (GlobalAppSetting.XrmWebApiBaseUrl.EndsWith ("/")) {
				address = GlobalAppSetting.XrmWebApiBaseUrl;
			} else {
				address = GlobalAppSetting.XrmWebApiBaseUrl + "/";
			}

#if DEBUG
			var url = GlobalAppSetting.IsHTML5Debug ? Path.Combine (address, "debug/index.html") :
				Path.Combine (FileSystemUtil.CachesFolder, "www/index.html");
#else
			var url = Path.Combine (FileSystemUtil.CachesFolder, "www/index.html");
#endif
			_webView.LoadRequest (new NSUrlRequest (new NSUrl (url)));//跳转自定义url

			View.AddSubview (_webView);
		}

		/// <summary>
		/// table类
		/// </summary>
		public class Source : UITableViewSource
		{
			UITextField _txtUserName, _txtPassword;
			CheckBoxView _chkIsRememberPassword;

			readonly CGRect _textRect;
			readonly CGRect _btnRect;
			readonly CGRect _dropdownList;
			UIButton _btnLogion;

			private static int loginFailedTimes = 0;
			private List<string> checkCodeList;
			UIImageView checkCode;

			private readonly LoginViewController _c;

			DropdownListView dropdownList;

			UIView viewCell;

			/// <summary>
			/// table的构造函数
			/// </summary>
			/// <param name="c">C.</param>
			public Source (LoginViewController c)
			{
				loginFailedTimes = 0;
				SystemSettingService.AddCleanupAction ("LoginViewController", () => InvokeOnMainThread (() => {
					GlobalAppSetting.UserCode = string.Empty;
					GlobalAppSetting.Password = string.Empty;
					GlobalAppSetting.HotelCD = string.Empty;
					GlobalAppSetting.IsRememberPassword = false;

					if (_txtUserName != null)
						_txtUserName.Text = string.Empty;
					if (_txtPassword != null)
						_txtPassword.Text = string.Empty;
					if (dropdownList != null)
						dropdownList.SelectedItem = new DropdownListViewItem {
							Text = "",
							Value = "",
							BrandCd = ""
						};
					if (_chkIsRememberPassword != null)
						_chkIsRememberPassword.IsChecked = false;
				}));

				_c = c;
				_textRect = new CGRect (UiStyleSetting.PaddingSizeLarge, 0, _c.View.Bounds.Width - UiStyleSetting.PaddingSizeLarge * 2, UiStyleSetting.HeightTextBox);
				_dropdownList = new CGRect (UiStyleSetting.PaddingSizeLarge, 0, _c.View.Bounds.Width - UiStyleSetting.PaddingSizeLarge * 2, UiStyleSetting.HeightTextBox);
				_btnRect = new CGRect (UiStyleSetting.PaddingSizeLarge, 0, _c.View.Bounds.Width - (UiStyleSetting.PaddingSizeLarge * 2), UiStyleSetting.HeightButton);

				var rect = new CGRect (0, 0, _c.View.Bounds.Width, UiStyleSetting.HeightTextBox + 2);
				viewCell = new UIView (rect);
				viewCell.BackgroundColor = UIColor.White;

				#region 是否启动TouchID验证
				if (GlobalAppSetting.isTouchID == true && GlobalAppSetting.UserCode != null) {
					LaunchTouchIDAuth ();
				}
				#endregion
			}

			/// <summary>
			/// 每行选中时执行
			/// </summary>
			public override async void RowSelected (UITableView tableView, NSIndexPath indexPath)
			{
				await Task.Run (() => InvokeOnMainThread (() => _c.View.EndEditing (true)));
			}

			/// <summary>
			/// 设置每行的高度
			/// </summary>
			public override nfloat GetHeightForRow (UITableView tableView, NSIndexPath indexPath)
			{

				if (indexPath.Section == 0) {
					if (indexPath.Row == 0)
						return _c._logoHeight;
					else if (indexPath.Row == 1)
						return 60;
					else
						return 60;
				} else if (indexPath.Section == 1) {
					if (indexPath.Row == 0)
						return 70;
					else
						return 40;
				} else {
					return 200;
				}
			}

			/// <summary>
			/// 每个section有几行
			/// </summary>
			public override nint RowsInSection (UITableView tableView, nint section)
			{
				if ((int)section == 0)
					return 4;
				else if ((int)section == 1)
					return 2;
				else
					return 1;
			}

			/// <summary>
			/// 设置有多少节
			/// </summary>
			public override nint NumberOfSections (UITableView tableView)
			{
				return 2;
			}

			public override UITableViewCell GetCell (UITableView tableView, NSIndexPath indexPath)
			{
				if (indexPath.Section == 0) {
					if (indexPath.Row == 0)
						return CreateLogoCell (tableView);
					if (indexPath.Row == 1)
						return CreateUserNameCell (tableView);
					if (indexPath.Row == 2)
						return CreatePasswordCell (tableView);
					return CreateHotelCodeCell (tableView);
				} else {
					if (indexPath.Row == 0)
						return CreateSettingCell (tableView);
					else
						return CreateLoginButtonCell (tableView);
				}
			}

			/// <summary>
			/// 创建用户名的cell
			/// </summary>
			private UITableViewCell CreateUserNameCell (UITableView tableView)
			{
				const string cellIdentifier = "TableViewCellUserName";
				var cell = tableView.DequeueReusableCell (cellIdentifier);
				if (cell == null) {
					cell = _c._builder.CreateTableViewCell4Input (tableView, cellIdentifier);
					_txtUserName = _c._builder.CreateIconTextBox4TableViewCell (cell, _textRect, "请输入用户账号",
						GlobalAppSetting.UserCode, "login_user.png");

					_txtUserName.EditingChanged += (sender, e) => {
						if (GlobalAppSetting.IsRememberPassword) {
							_txtPassword.Text = _txtUserName.Text != GlobalAppSetting.UserCode ? string.Empty : GlobalAppSetting.Password;
						}
					};

					_txtUserName.EditingDidEnd += async (sender, e) => {
						AlertUtil.ShowWaiting ("获取酒店编号...");
						//变更酒店编号
						var hotelInfo = await AuthenticationService.GetHotelInfo (_txtUserName.Text);
						if (hotelInfo != null) {
							viewCell.RemoveFromSuperview ();
							var item = new List<DropdownListViewItem> ();
							foreach (var hotel in hotelInfo.Data.Hotels) {
								item.Add (new DropdownListViewItem {
									Value = hotel.HotelCd,
									Text = hotel.HotelName,
									BrandCd = hotel.BrandCd
								});
							}
							dropdownList.Items = item;
							if (item.Count > 0) {
								GlobalAppSetting.HotelCD = item [0].Value;
								GlobalAppSetting.HotelName = item [0].Text;
								GlobalAppSetting.BrandCd = item [0].BrandCd;
							}
							dropdownList.SelectedItem = new DropdownListViewItem {
								Text = hotelInfo.Data.Hotels [0].HotelName,
								Value = hotelInfo.Data.Hotels [0].HotelCd,
								BrandCd = hotelInfo.Data.Hotels [0].BrandCd
							};
							dropdownList.delegateCallBack += (selectedItem) => {
								GlobalAppSetting.HotelCD = selectedItem.Value;
								GlobalAppSetting.HotelName = selectedItem.Text;
								GlobalAppSetting.BrandCd = selectedItem.BrandCd;
							};
						} else {
							dropdownList.SelectedItem = new DropdownListViewItem {
								Text = "",
								Value = "",
								BrandCd = ""
							};
							dropdownList.Items = null;
							GlobalAppSetting.HotelCD = "";
							GlobalAppSetting.HotelName = "";
							GlobalAppSetting.BrandCd = "";

							var indexPath = NSIndexPath.FromRowSection (3, 0);
							var cellfor = _c._tableView.CellAt (indexPath);
							cellfor.ContentView.AddSubview (viewCell);
						}
						AlertUtil.DismissWaiting ();
					};
				}

				return cell;
			}

			/// <summary>
			/// 创建密码的cell
			/// </summary>
			/// <returns>The password cell.</returns>
			/// <param name="tableView">Table view.</param>
			private UITableViewCell CreatePasswordCell (UITableView tableView)
			{
				const string cellIdentifier = "TableViewCellPassword";
				var cell = tableView.DequeueReusableCell (cellIdentifier);
				if (cell == null) {
					cell = _c._builder.CreateTableViewCell4Input (tableView, cellIdentifier);
					_txtPassword = _c._builder.CreateIconTextBox4TableViewCell (cell, _textRect, " 请输入密码",
						GlobalAppSetting.Password, "login_password.png");

					_txtPassword.SecureTextEntry = true;
				}
				return cell;
			}

			/// <summary>
			/// 酒店编码
			/// </summary>
			private UITableViewCell CreateHotelCodeCell (UITableView tableView)
			{
				const string cellIdentifier = "TableViewCellHotelCode";
				var cell = tableView.DequeueReusableCell (cellIdentifier);
				if (cell == null) {
					cell = new UITableViewCell (UITableViewCellStyle.Default, cellIdentifier) {
						SelectionStyle = UITableViewCellSelectionStyle.None
					};
					dropdownList = new DropdownListView (_dropdownList, "请选择酒店", _c, _c.dropdownListItem);
					if (_c.dropdownListItem != null) {
						dropdownList.SelectedItem = new DropdownListViewItem {
							Text = _c.dropdownListItem [0].Text,
							Value = _c.dropdownListItem [0].Value,
							BrandCd = _c.dropdownListItem [0].BrandCd
						};
						GlobalAppSetting.HotelCD = _c.dropdownListItem [0].Value;
						GlobalAppSetting.HotelName = _c.dropdownListItem [0].Text;
						GlobalAppSetting.BrandCd = _c.dropdownListItem [0].BrandCd;

						dropdownList.delegateCallBack += (selectedItem) => {
							GlobalAppSetting.HotelCD = selectedItem.Value;
							GlobalAppSetting.HotelName = selectedItem.Text;
							GlobalAppSetting.BrandCd = selectedItem.BrandCd;
						};
					} else {
						dropdownList.SelectedItem = new DropdownListViewItem {
							Text = "",
							Value = "",
							BrandCd = ""
						};
						dropdownList.Items = null;
						GlobalAppSetting.HotelCD = "";
						GlobalAppSetting.HotelName = "";
						GlobalAppSetting.BrandCd = "";
					}
					dropdownList.delegateCallBack += (selectedItem) => {
						GlobalAppSetting.HotelCD = selectedItem.Value;
						GlobalAppSetting.HotelName = selectedItem.Text;
						GlobalAppSetting.BrandCd = selectedItem.BrandCd;
					};
					var clearView = new UIView (_dropdownList);
					clearView.BackgroundColor = UIColor.Clear;
					clearView.AddGestureRecognizer (new BTTapGestureRecognizer (this, new Selector ("Taptap")));
					cell.ContentView.AddSubview (dropdownList);
					cell.ContentView.AddSubview (clearView);
					var borderView = new UIView (new CGRect (0, _textRect.Height, _c.View.Bounds.Width, 1F)) {
						BackgroundColor = UIColor.FromRGB (240, 240, 240)
					};
					cell.ContentView.Add (borderView);
					if (_c.dropdownListItem == null)
						cell.ContentView.AddSubview (viewCell);
				}

				return cell;
			}

			[Export ("Taptap")]
			public void Taptap ()
			{
				dropdownList.Tap ();
			}

			public class BTTapGestureRecognizer : UITapGestureRecognizer
			{
				public BTTapGestureRecognizer (NSObject target, Selector action) : base (target, action)
				{
					ShouldReceiveTouch += (sender, touch) => {
						return true;
					};
				}
			}

			[Export ("RefreshCheckCode")]
			public async void RefreshCheckCode ()
			{
				Random ra = new Random (unchecked((int)DateTime.Now.Ticks));
				checkCodeList = await AuthenticationService.GetCheckCode ((long)ra.NextDouble ());
				checkCode.Image = ImageUtil.ConvertBase64String2Image (checkCodeList [0]);
			}

			private async void LoginButtonClick (object sender, EventArgs e)
			{
				using (var t = new Toast ()) {
					try {

						var cameraView = new CustomerCameraViewController ();
						_c.NavigationController.PushViewController (cameraView, true);

						return;


						_btnLogion.Enabled = false;
						//t.ProgressWaiting ("正在登录...");

						var userName = _txtUserName.Text;
						if (dropdownList.SelectedItem != null && !string.IsNullOrWhiteSpace (dropdownList.SelectedItem.Text)) {
							userName += "&" + GlobalAppSetting.HotelCD;
						}
						var userModel = new UserModel {
							uid = userName,
							pwd = EncryptionUtil.DESDefaultEncryption (_txtPassword.Text)
						};
						if (loginFailedTimes >= 3) {
							Random ra = new Random (unchecked((int)DateTime.Now.Ticks));
							checkCodeList = await AuthenticationService.GetCheckCode ((long)ra.NextDouble ());
							var alertAction = UIAlertController.Create ("您多次尝试登录未成功，请输入验证码", "", UIAlertControllerStyle.Alert);
							alertAction.AddAction (UIAlertAction.Create ("确认", UIAlertActionStyle.Default, alert => {
								var modelUser = new UserModel {
									uid = userName,
									pwd = EncryptionUtil.DESDefaultEncryption (_txtPassword.Text),
									checkCode = alertAction.TextFields [0].Text,
									verifyStr = checkCodeList [2]
								};
								//AlertUtil.ShowWaiting ("正在登录...");
								LoginAction (modelUser);
								_btnLogion.Enabled = true;
								//AlertUtil.DismissWaiting ();
							}));
							alertAction.AddAction (UIAlertAction.Create ("取消", UIAlertActionStyle.Cancel, cancel => { }));
							alertAction.AddTextField ((field) => {
								checkCode = new UIImageView (new CGRect (0, 0, _c.View.Bounds.Width / 4, 20));
								checkCode.Image = ImageUtil.ConvertBase64String2Image (checkCodeList [0]);
								field.RightView = new UIView (new CGRect (0, 0, _c.View.Bounds.Width / 4, 20));
								field.RightView.AddGestureRecognizer (new BTTapGestureRecognizer (this, new Selector ("RefreshCheckCode")));
								field.RightView.AddSubview (checkCode);
								field.RightViewMode = UITextFieldViewMode.Always;
								field.Placeholder = "请输入验证码";
							});
							_c.PresentViewController (alertAction, true, null);

							loginFailedTimes += 1;
							_btnLogion.Enabled = true;
							return;
						}
						//AlertUtil.ShowWaiting ("正在登录...");
						LoginAction (userModel);
						_btnLogion.Enabled = true;
						//AlertUtil.DismissWaiting ();
					} catch (Exception ex) {
						AlertUtil.Error (ex.Message);
						_btnLogion.Enabled = true;
					}
				}
			}

			private async void LoginAction (UserModel userModel)
			{
				AlertUtil.ShowWaiting ("正在登录...");
				var authUser = await AuthenticationService.LoginAsync (userModel);
				// 登录失败
				if (authUser == null || authUser.SSOToken == null) {
					loginFailedTimes += 1;
					_btnLogion.Enabled = true;
					AlertUtil.DismissWaiting ();
					return;
				}

				GlobalAppSetting.XrmAuthToken = authUser.AuthToken;
				GlobalAppSetting.UserCode = _txtUserName.Text;
				GlobalAppSetting.DomainUserCode = authUser.UserCode;

				GlobalAppSetting.UserId = authUser.SystemUserId;
				if (GlobalAppSetting.IsRememberPassword)
					GlobalAppSetting.Password = _txtPassword.Text;

				// 从登陆接口获取Sso Token 和过期时间 和 RefreshToken 和 登录时间
				GlobalAppSetting.SsoToken = authUser.SSOToken.AccessToken;
				GlobalAppSetting.SsoTokenExpiredTime = authUser.SSOToken.ExpiresIn;
				GlobalAppSetting.ReFreshToken = authUser.SSOToken.RefreshToken;
				GlobalAppSetting.LoginTime = DateTime.Now.ToString ("yyyy-MM-dd HH:mm:ss");

				new PushService ().Login (GlobalAppSetting.UserId, _txtPassword.Text);

				await VersionService.TryUpgradeWww ();

				var needUpdateIos = await VersionService.TryUpgradeIos ();
				if (needUpdateIos) {
					UpdateIosClient ();
					_btnLogion.Enabled = true;
					AlertUtil.DismissWaiting ();
					return;
				}

				//_c.PreloadWebResource ();

				var connected = await AuthenticationService.Logon ();

				// 如果登录人员是服务员，则直接跳转到服务员的菜单页面
				var type = Convert.ToInt32 (await AuthenticationService.CheckTheUserType (_txtUserName.Text));
				// type=1表示当前用户为服务员
				if (connected && (type == 1 || type == 2)) {
					// 跳转到服务员菜单页面
					var wvc = new WebViewController () { _menuUrl = "roomControl/card" };
					_c.NavigationController.PushViewController (wvc, true);
					_btnLogion.Enabled = true;
					AlertUtil.DismissWaiting ();
					return;
				}

				// 获取聊天服务器的的配置信息
				//var chatConfig = await AuthenticationService.GetChatServiceInfo ();

				// 初始化聊天服务器       聊天服务器地址，主机名，端口号，XrmWebApiBaseUrl
				//ChatClient.Initialize (chatConfig.Host, chatConfig.ServiceName, chatConfig.Port, 
				//	GlobalAppSetting.XrmWebApiBaseUrl, 
				//	GlobalAppSetting.XrmAuthToken, 
				//	GlobalAppSetting.UserCode, 
				//	GlobalAppSetting.Password);

				// 登录聊天服务器
				//var isSuccess = await ChatClient.Logon ();

				if (/*isSuccess &&*/ connected) {

					// 判断是HMS用户还是PMS用户
					var items = dropdownList.Items;
					if (items != null && items.Count > 1) {
						GlobalAppSetting.IsPMSUser = true;
					} else {
						GlobalAppSetting.IsPMSUser = false;
					}

					//从接口获取数据插入数据表中
					await MenuService.LoadMenusToSqlLite ();

					ContactsService.StartSyncContact ();
					AuthTokenRefreshService.StartRefreshToken ();

					_c.NavigationController.PopViewController (false);
				}
				AlertUtil.DismissWaiting ();
			}

			private void UpdateIosClient ()
			{
				var baseUrl = GlobalAppSetting.XrmWebApiBaseUrl;
				if (!baseUrl.ToLower ().Contains ("https")) {
					baseUrl = baseUrl.ToLower ().Replace ("http", "https");
				}
				if (!baseUrl.EndsWith ("/")) {
					baseUrl += "/";
				}
				string path = "itms-services://?action=download-manifest&url="
							  + baseUrl
							  + "csupdate/ios/ios.plist";
				UIApplication.SharedApplication.OpenUrl (new NSUrl (path));
			}

			/// <summary>
			/// 创建登录按钮的cell
			/// </summary>
			/// <returns>The login button cell.</returns>
			/// <param name="tableView">Table view.</param>
			private UITableViewCell CreateLoginButtonCell (UITableView tableView)
			{
				const string cellIdentifier = "TableViewCellLoginButton";
				UITableViewCell cell = tableView.DequeueReusableCell (cellIdentifier);

				if (cell == null) {
					cell = new UITableViewCell (UITableViewCellStyle.Default, cellIdentifier);
					cell.SelectionStyle = UITableViewCellSelectionStyle.None;
					_btnLogion = _c._builder.CreateButton4TableViewCell (cell, _btnRect, "登 录");
					_btnLogion.TouchUpInside += LoginButtonClick;
				}

				return cell;
			}

			/// <summary>
			/// 创建显示logo的cell
			/// </summary>
			/// <returns>The logo cell.</returns>
			/// <param name="tableView">Table view.</param>
			private UITableViewCell CreateLogoCell (UITableView tableView)
			{
				const string cellIdentifier = "TableViewCellLogoLabel";
				UITableViewCell cell = tableView.DequeueReusableCell (cellIdentifier);

				if (cell == null) {
					cell = new UITableViewCell (UITableViewCellStyle.Default, cellIdentifier);
					var scale = tableView.Frame.Width / _c._logo.Size.Width;
					UIImageView imageView = new UIImageView (new CGRect (0, 0, _c._logo.Size.Width * scale, _c._logo.Size.Height * scale));
					imageView.Image = _c._logo;
					cell.ContentView.Add (imageView);
				}
				return cell;
			}

			/// <summary>
			/// 创建显示标语的cell
			/// </summary>
			/// <returns>The subject cell.</returns>
			/// <param name="tableView">Table view.</param>
			private UITableViewCell CreateSettingCell (UITableView tableView)
			{
				const string cellIdentifier = "TableViewCellsSubjectLabel";
				UITableViewCell cell = tableView.DequeueReusableCell (cellIdentifier);

				if (cell == null) {
					cell = new UITableViewCell (UITableViewCellStyle.Default, cellIdentifier);
					cell.SelectionStyle = UITableViewCellSelectionStyle.None;

					#region CheckBox Remember Password
					_chkIsRememberPassword = new CheckBoxView (new CGRect (UiStyleSetting.PaddingSizeLarge, 25, 120, 20),
						"记住密码", GlobalAppSetting.IsRememberPassword);
					_chkIsRememberPassword.TouchUpInside += (sender, e) => {
						GlobalAppSetting.IsRememberPassword = _chkIsRememberPassword.IsChecked;
						if (!GlobalAppSetting.IsRememberPassword) {
							GlobalAppSetting.Password = string.Empty;
						}
					};
					cell.AddSubview (_chkIsRememberPassword);
					#endregion

					#region 忘记密码 Button
					var settingSeverBtn = new IconButtonView (new CGRect (tableView.Frame.Width - 120, 20, 100, 30),
											  "忘记密码", "login_setting.png");
					settingSeverBtn.SetTitleColor (UiStyleSetting.RektecBlueColor, UIControlState.Normal);
					settingSeverBtn.TouchUpInside += (sender, e) => {
						//						_c.NavigationController.PushViewController (new SeverAddressEditViewController (new RootElement ("服务器地址"),
						//							"服务器",
						//							GlobalAppSetting.XrmWebApiBaseUrl), true);
						// 链接到忘记密码的H5页面
						//var wvc = new WebViewController () { _menuUrl = GlobalAppSetting.XrmWebApiBaseUrl + "m/index.html#/password/forget" };
						var wvc = new WebViewController () { _menuUrl = "password/forget", _special = Path.Combine (NSBundle.MainBundle.ResourcePath, "www/index.html") };
						//_c.NavigationController.NavigationBarHidden = true;
						_c.NavigationController.PushViewController (wvc, false);
					};
					cell.AddSubview (settingSeverBtn);
					#endregion

				}
				return cell;
			}

			#region 启动TouchID验证
			/// <summary>
			/// 启动TouchID验证
			/// </summary>
			public async void LaunchTouchIDAuth ()
			{
				NSError AuthError;
				LAContext context = new LAContext ();
				// 预加载 Web 资源文件
				_c.PreloadWebResource ();

				// 如果登录人员是服务员，则直接跳转到服务员的菜单页面
				var type = Convert.ToInt32 (await AuthenticationService.CheckTheUserType (_txtUserName.Text));


				var authUser = await AuthenticationService.LoginAsync (new UserModel () {
					uid = _txtUserName.Text,
					pwd = EncryptionUtil.DESDefaultEncryption (_txtPassword.Text)
				});

				if (authUser == null) {
					_btnLogion.Enabled = true;
					return;
				}

				GlobalAppSetting.XrmAuthToken = authUser.AuthToken;
				GlobalAppSetting.UserCode = _txtUserName.Text;//xxpang
				GlobalAppSetting.DomainUserCode = authUser.UserCode;//homeinns\xxpang
				var connected = await AuthenticationService.Logon ();
				////用户在数据库中的主键
				GlobalAppSetting.UserId = authUser.SystemUserId;
				if (GlobalAppSetting.IsRememberPassword)
					GlobalAppSetting.Password = _txtPassword.Text;

				// 注册通知推送服务
				//new PushService ().Login (GlobalAppSetting.UserId, _txtPassword.Text);
				// H5版本检查更新
				await VersionService.TryUpgradeWww ();
				// iOS版本检查更新
				var needUpdateIos = await VersionService.TryUpgradeIos ();
				if (needUpdateIos) {
					UpdateIosClient ();
					_btnLogion.Enabled = true;
					return;
				}

				// 再次确认设备是否支持 Touch ID·
				if (context.CanEvaluatePolicy (LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out AuthError)) {
					var replyHandler = new LAContextReplyHandler ((success, error) => {
						InvokeOnMainThread (() => {
							if (success) {
								// 验证成功，导航到应用主界面

								// type=1表示当前用户为服务员
								if (connected && (type == 1 || type == 2)) {
									GlobalAppSetting.isTouchID = false;
									//_btnLogion.Enabled = true;
									// 跳转到服务员菜单页面
									var wvc = new WebViewController () { _menuUrl = "roomControl/card" };
									_c.NavigationController.PushViewController (wvc, true);
									_btnLogion.Enabled = true;
									return;
								}

								if (connected) {
									ContactsService.StartSyncContact ();
									_c.NavigationController.PopViewController (false);
								}
								_btnLogion.Enabled = true;

							} else {
								_btnLogion.Enabled = true;
							}
						});
					});
					context.LocalizedFallbackTitle = "验证密码登录";
					context.EvaluatePolicy (LAPolicy.DeviceOwnerAuthenticationWithBiometrics, "通过Home键验证已有手机指纹", replyHandler);
					var PMSType = await AuthenticationService.CheckPMSType ("");
					var items = JsonConvert.DeserializeObject<IsPMSType> (PMSType);
					GlobalAppSetting.IsPmsType = items.LabelCd == null ? "null" : items.LabelCd;
				} else {
					_btnLogion.Enabled = true;
					var errorAlertController = UIAlertController.Create ("提示", "TouchID暂不可用，请使用密码登录！", UIAlertControllerStyle.Alert);
					errorAlertController.AddAction (UIAlertAction.Create ("好的", UIAlertActionStyle.Default, null));
					_c.PresentViewController (errorAlertController, true, null);
				}
			}
			#endregion
		}
	}
}

