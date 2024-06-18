using HarmonyLib;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;

namespace Brrainz
{
	public static class CrossPromotion
	{
		const string _crosspromotion = "brrainz-crosspromotion";
		internal static ulong userID;

		internal static List<SteamUGCDetails_t> promotionMods = new List<SteamUGCDetails_t>();
		internal static Dictionary<ulong, bool?> allVoteStati = new Dictionary<ulong, bool?>();
		internal static Dictionary<ulong, Texture2D> previewTextures = new Dictionary<ulong, Texture2D>();
		internal static List<ulong> subscribingMods = new List<ulong>();
		internal static ulong? lastPresentedMod = null;

		public static void Install(ulong userID)
		{
			CrossPromotion.userID = userID;

			if (Harmony.HasAnyPatches(_crosspromotion))
				return;

			var instance = new Harmony(_crosspromotion);

			_ = instance.Patch(
				SymbolExtensions.GetMethodInfo(() => ModLister.RebuildModList()),
				postfix: new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => ModLister_RebuildModList_Postfix()))
			);

			_ = instance.Patch(
				SymbolExtensions.GetMethodInfo(() => new Page_ModsConfig().PostClose()),
				postfix: new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => Page_ModsConfig_PostClose_Postfix()))
			);

			_ = instance.Patch(
				SymbolExtensions.GetMethodInfo(() => WorkshopItems.Notify_Subscribed(default)),
				postfix: new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => WorkshopItems_Notify_Subscribed_Postfix(new PublishedFileId_t(0))))
			);

			_ = instance.Patch(
				SymbolExtensions.GetMethodInfo(() => new Page_ModsConfig().DoModInfo(default, default)),
				prefix: new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => Page_ModsConfig_DoModInfo_Prefix(default, default, default)))
			);
		}

		static void ModLister_RebuildModList_Postfix()
		{
			_ = ModPreviewPath(0);
			new Thread(() => { FetchPromotionMods(); }).Start();
		}

		static void Page_ModsConfig_PostClose_Postfix()
		{
			subscribingMods.Clear();
		}

		static void WorkshopItems_Notify_Subscribed_Postfix(PublishedFileId_t pfid)
		{
			var longID = pfid.m_PublishedFileId;

			if (subscribingMods.Contains(longID) == false)
				return;
			_ = subscribingMods.Remove(longID);

			LongEventHandler.ExecuteWhenFinished(() =>
			{
				var mod = ModLister.AllInstalledMods.FirstOrDefault(meta => meta.GetPublishedFileId().m_PublishedFileId == longID);
				if (mod == null)
					return;

				ModsConfig.SetActive(mod, true);
				ModsConfig.Save();

				Find.WindowStack.Add(new MiniDialog(mod.Name + " added"));
			});
		}

		static bool Page_ModsConfig_DoModInfo_Prefix(Page_ModsConfig __instance, Rect r, ModMetaData mod)
		{
			if (mod == null
				|| mod.GetWorkshopItemHook().steamAuthor.m_SteamID != userID
				|| promotionMods.Count == 0)
				return true;

			return PromotionLayout.Promotion(r, __instance) == false;
		}

		internal static string ModPreviewPath(ulong modID)
		{
			var dir = Path.GetTempPath() + "BrrainzMods" + Path.DirectorySeparatorChar;
			if (Directory.Exists(dir) == false)
				_ = Directory.CreateDirectory(dir);
			return dir + modID + "-preview.jpg";
		}

		internal static byte[] SafeRead(string path)
		{
			for (var i = 1; i <= 5; i++)
			{
				try
				{
					return File.ReadAllBytes(path);
				}
				catch (Exception)
				{
					Thread.Sleep(250);
				}
			}
			return null;
		}

		internal static Texture2D PreviewForMod(ulong modID)
		{
			if (previewTextures.TryGetValue(modID, out var texture))
				return texture;

			var path = ModPreviewPath(modID);
			if (File.Exists(path) == false)
				return null;

			texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
			if (texture.LoadImage(SafeRead(path)))
				previewTextures[modID] = texture;

			return texture;
		}

		internal static void UpdateVotingStatus(ulong modID, Action<GetUserItemVoteResult_t, bool> callback)
		{
			var callDelegate = new CallResult<GetUserItemVoteResult_t>.APIDispatchDelegate(callback);
			var call = SteamUGC.GetUserItemVote(new PublishedFileId_t(modID));
			var resultHandle = CallResult<GetUserItemVoteResult_t>.Create(callDelegate);
			resultHandle.Set(call, null);
		}

		static void AsyncUserModsQuery(UGCQueryHandle_t query, Action<SteamUGCQueryCompleted_t, bool> callback)
		{
			var callDelegate = new CallResult<SteamUGCQueryCompleted_t>.APIDispatchDelegate((result, failure) =>
			{
				callback(result, failure);
				_ = SteamUGC.ReleaseQueryUGCRequest(query);
			});
			var call = SteamUGC.SendQueryUGCRequest(query);
			var resultHandle = CallResult<SteamUGCQueryCompleted_t>.Create(callDelegate);
			resultHandle.Set(call, null);
		}

		static void AsyncDownloadQuery(UGCHandle_t content, string path, Action<RemoteStorageDownloadUGCResult_t, bool> callback)
		{
			var callDelegate = new CallResult<RemoteStorageDownloadUGCResult_t>.APIDispatchDelegate(callback);
			var call = SteamRemoteStorage.UGCDownloadToLocation(content, path, 0);
			var resultHandle = CallResult<RemoteStorageDownloadUGCResult_t>.Create(callDelegate);
			resultHandle.Set(call, null);
		}

		public static void FetchPromotionMods()
		{
			if (SteamManager.Initialized == false)
				return;

			var rimworldID = SteamUtils.GetAppID();
			var aID = new AccountID_t(unchecked((uint)userID));

			var itemQuery = SteamUGC.CreateQueryUserUGCRequest(aID,
				EUserUGCList.k_EUserUGCList_Published, EUGCMatchingUGCType.k_EUGCMatchingUGCType_UsableInGame,
				EUserUGCListSortOrder.k_EUserUGCListSortOrder_VoteScoreDesc, rimworldID, rimworldID, 1);

			try
			{
				_ = SteamUGC.SetReturnLongDescription(itemQuery, true);
				_ = SteamUGC.SetRankedByTrendDays(itemQuery, 7);
			}
			catch
			{
			}

			AsyncUserModsQuery(itemQuery, (result, failure) =>
			{
				for (var i = (uint)0; i < result.m_unNumResultsReturned; i++)
					if (SteamUGC.GetQueryUGCResult(result.m_handle, i, out var mod))
						if (promotionMods.Any(m => m.m_nPublishedFileId.m_PublishedFileId == mod.m_nPublishedFileId.m_PublishedFileId) == false)
						{
							promotionMods.Add(mod);
							var modID = mod.m_nPublishedFileId.m_PublishedFileId;

							var path = ModPreviewPath(modID);
							if (File.Exists(path) == false || new FileInfo(path).Length != mod.m_nPreviewFileSize)
							{
								AsyncDownloadQuery(mod.m_hPreviewFile, path, (result2, failure2) =>
								{
									if (File.Exists(path))
									{
										if (previewTextures.ContainsKey(modID))
											_ = previewTextures.Remove(modID);
									}
								});
							}

							UpdateVotingStatus(modID, (result2, failure2) =>
							{
								allVoteStati[modID] = (result2.m_eResult == EResult.k_EResultOK) ? result2.m_bVotedUp : (bool?)null;
							});
						}
			});
		}
	}

	[StaticConstructorOnStartup]
	internal class PromotionLayout
	{
		internal static bool Promotion(Rect mainRect, Page_ModsConfig page)
		{
			if (SteamManager.Initialized == false)
				return false;

			var mod = page.primarySelectedMod;
			if (mod == null
				|| mod.GetWorkshopItemHook().steamAuthor.m_SteamID != CrossPromotion.userID
				|| CrossPromotion.promotionMods.Count == 0)
				return false;

			var leftColumn = mainRect.width * 2 / 3;
			var rightColumn = mainRect.width - leftColumn - 10f;

			GUI.BeginGroup(mainRect);

			try
			{
				ContentPart(mainRect, leftColumn, mod, page);
				PromotionPart(mainRect, leftColumn, rightColumn, mod, page);
			}
			catch (Exception)
			{
				GUI.EndGroup();
				return false;
			}

			GUI.EndGroup();
			return true;
		}

		static Vector2 leftScroll = Vector2.zero;
		static Vector2 rightScroll = Vector2.zero;

		static List<FloatMenuOption> GetAdvancedMenu(Page_ModsConfig page, ModMetaData mod)
		{
			var list = new List<FloatMenuOption>();
			if (SteamManager.Initialized && mod.OnSteamWorkshop)
			{
				list.Add(new FloatMenuOption("Unsubscribe".Translate(), delegate ()
				{
					var windowStack = Find.WindowStack;
					var text3 = "ConfirmUnsubscribeFrom".Translate(mod.Name);
					void confirmedAct()
					{
						mod.enabled = false;
						Workshop.Unsubscribe(mod);
					};
					windowStack.Add(Dialog_MessageBox.CreateConfirmation(text3, confirmedAct, true, null, WindowLayer.Dialog));
				}, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
				list.Add(new FloatMenuOption("WorkshopPage".Translate(), delegate ()
				{
					SteamUtility.OpenWorkshopPage(mod.GetPublishedFileId());
				}, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
				if (!mod.Official && (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor))
				{
					list.Add(new FloatMenuOption("ModFolder".Translate(), delegate ()
					{
						Application.OpenURL(mod.RootDir.FullName);
					}, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
				}
			}
			else
			{
				if (!mod.Url.NullOrEmpty())
				{
					list.Add(new FloatMenuOption("ModWebsite".Translate(), delegate ()
					{
						Application.OpenURL(mod.Url);
					}, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
				}
				if (!mod.Official && (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor))
				{
					list.Add(new FloatMenuOption("ModFolder".Translate(), delegate ()
					{
						Application.OpenURL(GenFilePaths.ModsFolderPath + "/" + mod.FolderName);
					}, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
				}
			}
			if (page.primaryModHandle != null && !page.primaryModHandle.SettingsCategory().NullOrEmpty())
			{
				list.Add(new FloatMenuOption("ModOptions".Translate(), delegate ()
				{
					Find.WindowStack.Add(new Dialog_ModSettings(page.primaryModHandle));
				}, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
			}
			if (Prefs.DevMode && SteamManager.Initialized && mod.CanToUploadToWorkshop())
			{
				list.Add(new FloatMenuOption(Workshop.UploadButtonLabel(mod.GetPublishedFileId()), delegate ()
				{
					var loadFolders = mod.loadFolders;
					var list2 = loadFolders?.GetIssueList(mod);
					if (mod.HadIncorrectlyFormattedVersionInMetadata)
					{
						Messages.Message("MessageModNeedsWellFormattedTargetVersion".Translate(VersionControl.CurrentMajor + "." + VersionControl.CurrentMinor), MessageTypeDefOf.RejectInput, false);
						return;
					}
					if (mod.HadIncorrectlyFormattedPackageId)
					{
						Find.WindowStack.Add(new Dialog_MessageBox("MessageModNeedsWellFormattedPackageId".Translate(), null, null, null, null, null, false, null, null, WindowLayer.Dialog));
						return;
					}
					if (!list2.NullOrEmpty<string>())
					{
						Find.WindowStack.Add(new Dialog_MessageBox("ModHadLoadFolderIssues".Translate() + "\n" + list2.ToLineList("  - "), null, null, null, null, null, false, null, null, WindowLayer.Dialog));
						return;
					}
					var windowStack = Find.WindowStack;
					var mod2 = mod;
					void acceptAction()
					{
						SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
						var text3 = "ConfirmContentAuthor".Translate();
						void confirmedAct()
						{
							SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
							Workshop.Upload(mod);
						}
						var dialog_MessageBox = Dialog_MessageBox.CreateConfirmation(text3, confirmedAct, true, null, WindowLayer.Dialog);
						dialog_MessageBox.buttonAText = "Yes".Translate();
						dialog_MessageBox.buttonBText = "No".Translate();
						dialog_MessageBox.interactionDelay = 6f;
						Find.WindowStack.Add(dialog_MessageBox);
					}
					windowStack.Add(new Dialog_ConfirmModUpload(mod2, acceptAction));
				}, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
			}
			return list;
		}

		static string ConvertHTML(ModMetaData mod, string otherDescription = null)
		{
			var description = otherDescription;
			if (description == null)
			{
				var mainModID = mod.GetPublishedFileId().m_PublishedFileId;
				var promoMods = CrossPromotion.promotionMods.ToArray();
				var thisMod = promoMods.FirstOrDefault(m => m.m_nPublishedFileId.m_PublishedFileId == mainModID);

				description = thisMod.m_rgchDescription;
				if (description == null || description.Length == 0)
					description = mod.Description;
			}

			var divider = description.IndexOf("[hr][/hr]");
			if (divider == -1)
				return description;

			description = description.Substring(0, divider);

			description += @"[b]MY OTHER MODS[/b]
I make RimWorld mods since 2015. Check out all my mods here to right. You can sub directly from the icons by toggling the red cross or click on them for more info!

[b]CONTACT[/b]
Andreas Pardeike (aka Brrainz)
Email: andreas@pardeike.net
Discord: <color=blue>https://discord.gg/DsFxX5PG67</color>
Twitter: <color=blue>https://twitter.com/pardeike</color>
Twitch: <color=blue>https://twitch.tv/brrainz</color>";

			description = description.Replace("[b]", "<b><color=yellow>");
			description = description.Replace("[/b]", "</color></b>");
			description = description.Replace("[i]", "<i><color=silver>");
			description = description.Replace("[/i]", "</color></i>");
			return description;
		}

		static void ContentPart(Rect mainRect, float leftColumn, ModMetaData mod, Page_ModsConfig page)
		{
			var mainModID = mod.GetPublishedFileId().m_PublishedFileId;
			var promoMods = CrossPromotion.promotionMods.ToArray();

			if (CrossPromotion.lastPresentedMod != mainModID)
			{
				leftScroll = Vector2.zero;
				rightScroll = Vector2.zero;
				CrossPromotion.lastPresentedMod = mainModID;

				new Thread(() =>
				{
					foreach (var promoMod in promoMods)
						CrossPromotion.UpdateVotingStatus(promoMod.m_nPublishedFileId.m_PublishedFileId, (result2, failure2) =>
						{
							CrossPromotion.allVoteStati[promoMod.m_nPublishedFileId.m_PublishedFileId] = (result2.m_eResult == EResult.k_EResultOK) ? result2.m_bVotedUp : (bool?)null;
						});
				})
				.Start();
			}

			var outRect = new Rect(0f, 6f, leftColumn, mainRect.height - 30f - 10f - 6f);
			var width = outRect.width - 20f;
			var imageRect = new Rect(0f, 0f, width, width * mod.PreviewImage.height / mod.PreviewImage.width);

			var style = new GUIStyle { richText = true, wordWrap = true };
			var content = new GUIContent($"<color=white>{ConvertHTML(mod)}</color>");
			var height = style.CalcHeight(content, width);
			var textRect = new Rect(0f, 24f + 10f + imageRect.height + 2f, width, height - 2f);
			var innerRect = new Rect(0f, 0f, width, imageRect.height + 20f + 8f + 10f + textRect.height);

			Widgets.BeginScrollView(outRect, ref leftScroll, innerRect, true);
			GUI.DrawTexture(imageRect, mod.PreviewImage, ScaleMode.ScaleToFit);
			var widgetRow = new WidgetRow(imageRect.xMax, imageRect.yMax + 8f, UIDirection.LeftThenDown, width, 8f);
			if (widgetRow.ButtonText("MoreActions".Translate(), null, true))
			{
				SoundDefOf.Click.PlayOneShotOnCamera(null);
				Find.WindowStack.Add(new FloatMenu(GetAdvancedMenu(page, mod)));
			}
			if (widgetRow.ButtonText(mod.Active ? "Disable".Translate() : "Enable".Translate(), null, true, true, true, null))
			{
				SoundDefOf.Click.PlayOneShotOnCamera(null);
				if (mod.Active)
				{
					page.TrySetModInactive(mod);
				}
				else
				{
					page.TrySetModActive(mod);
				}
				page.selectedMods.Clear();
				page.selectedMods.Add(page.primarySelectedMod);
			}
			if (mod.ModVersion.NullOrEmpty() == false)
			{
				var anchor = Text.Anchor;
				Text.Anchor = TextAnchor.MiddleLeft;
				widgetRow.Label($"v{mod.ModVersion}", -1, null, 24f);
				Text.Anchor = anchor;
			}
			GUILayout.BeginArea(textRect);
			GUILayout.Label(content, style);
			GUILayout.EndArea();
			Widgets.EndScrollView();
		}

		static void PromotionPart(Rect mainRect, float leftColumn, float rightColumn, ModMetaData mod, Page_ModsConfig page)
		{
			var mainModID = mod.GetPublishedFileId();

			Text.Font = GameFont.Tiny;
			var headerHeight = 30f;
			var headerRect = new Rect(leftColumn + 10f, -4f, rightColumn - 20f, headerHeight);
			Text.Anchor = TextAnchor.UpperCenter;
			Widgets.Label(headerRect, "Mods of " + mod.AuthorsString.Replace("Andreas Pardeike", "Brrainz") + ":".Truncate(headerRect.width, null));
			Text.Anchor = TextAnchor.UpperLeft;

			var outRect = new Rect(leftColumn + 10f, headerHeight - 4f, rightColumn, mainRect.height - (headerHeight - 4f) - 30f - 10f);
			var width = outRect.width - 20f;
			var previewHeight = width * 319f / 588f;
			var promoMods = CrossPromotion.promotionMods.ToArray().Where(m => m.m_nPublishedFileId != mainModID);
			var workshopMods = WorkshopItems.AllSubscribedItems.Select(wi => wi.PublishedFileId.m_PublishedFileId).ToList();
			var activeMods = ModLister.AllInstalledMods.Where(meta => meta.Active).Select(meta => meta.GetPublishedFileId().m_PublishedFileId).ToList();

			var height = 0f;
			foreach (var promoMod in promoMods)
			{
				var myModID = promoMod.m_nPublishedFileId.m_PublishedFileId;
				var isLocalFile = ModLister.AllInstalledMods.Any(meta => meta.GetPublishedFileId().m_PublishedFileId == myModID && meta.Source == ContentSource.ModsFolder);
				var isSubbed = workshopMods.Contains(myModID);
				_ = CrossPromotion.allVoteStati.TryGetValue(myModID, out var voteStatus);

				if (height > 0)
					height += 10f;

				var preview = CrossPromotion.PreviewForMod(promoMod.m_nPublishedFileId.m_PublishedFileId);
				if (preview != null)
				{
					height += width * preview.height / preview.width + 2f;
					if (isLocalFile == false && (isSubbed == false || (voteStatus == false)))
						height += 16f;
				}
			}

			Widgets.BeginScrollView(outRect, ref rightScroll, new Rect(0f, 0f, width, height), true);
			var firstTime = true;
			var modRect = new Rect(0f, 0f, width, 0f);
			foreach (var promoMod in promoMods)
			{
				var myModID = promoMod.m_nPublishedFileId.m_PublishedFileId;
				var isLocalFile = ModLister.AllInstalledMods.Any(meta => meta.GetPublishedFileId().m_PublishedFileId == myModID && meta.Source == ContentSource.ModsFolder);
				var isSubbed = workshopMods.Contains(myModID);
				var isActive = activeMods.Contains(myModID);
				_ = CrossPromotion.allVoteStati.TryGetValue(myModID, out var voteStatus);

				if (firstTime == false)
					modRect.y += 10f;

				var preview = CrossPromotion.PreviewForMod(promoMod.m_nPublishedFileId.m_PublishedFileId);
				if (preview != null)
				{
					modRect.height = width * preview.height / preview.width;
					GUI.DrawTexture(modRect, preview, ScaleMode.ScaleToFit);

					var checkRect = modRect;
					checkRect.xMax -= 4f;
					checkRect.yMax -= 4f;
					checkRect.xMin = checkRect.xMax - 18f;
					checkRect.yMin = checkRect.yMax - 18f;
					var active = isActive;
					GUI.DrawTexture(checkRect.ContractedBy(-2f), CheckboxBackground);
					Widgets.Checkbox(checkRect.xMin, checkRect.yMin, ref active, checkRect.width);
					if (active != isActive)
					{
						var clickedMod = ModLister.AllInstalledMods.FirstOrDefault(meta => meta.GetPublishedFileId().m_PublishedFileId == myModID);
						if (clickedMod != null)
						{
							ModsConfig.SetActive(clickedMod, active);
							ModsConfig.Save();
						}
					}

					if (Mouse.IsOver(checkRect) == false)
					{
						Widgets.DrawHighlightIfMouseover(modRect);
						if (Widgets.ButtonInvisible(modRect, true))
						{
							var useSelect = isSubbed || isLocalFile;
							var actionButton = useSelect ? "Select" : "Subscribe";
							void actionButtonAction()
							{
								if (useSelect)
								{
									var clickedMod = ModLister.AllInstalledMods.FirstOrDefault(meta => meta.GetPublishedFileId().m_PublishedFileId == myModID);
									page.SelectMod(clickedMod);
								}
								else
								{
									new Thread(() =>
									{
										CrossPromotion.subscribingMods.Add(myModID);
										_ = SteamUGC.SubscribeItem(new PublishedFileId_t(myModID));
									})
									.Start();
								}
							}
							var infoWindow = new Dialog_MessageBox(ConvertHTML(mod, promoMod.m_rgchDescription), "Close".Translate(), null, actionButton, actionButtonAction, null, false, null, null);
							Find.WindowStack.Add(infoWindow);
						}
					}
					modRect.y += modRect.height + 2f;

					modRect.height = 0f;
					if (isLocalFile == false)
					{
						if (isSubbed == false)
						{
							modRect.height = 16f;
							if (CrossPromotion.subscribingMods.Contains(myModID))
								Widgets.Label(modRect, WaitingString);
							else if (Widgets.ButtonText(modRect, "Subscribe", false, true, true))
								new Thread(() =>
								{
									CrossPromotion.subscribingMods.Add(myModID);
									_ = SteamUGC.SubscribeItem(new PublishedFileId_t(myModID));
								}).Start();
						}
						else if (voteStatus != null && voteStatus == false)
						{
							modRect.height = 16f;
							if (Widgets.ButtonText(modRect, "Like", false, true, true))
							{
								new Thread(() =>
								{
									CrossPromotion.allVoteStati[myModID] = true;
									_ = SteamUGC.SetUserItemVote(new PublishedFileId_t(myModID), true);
								}).Start();
							}
						}
					}
					modRect.y += modRect.height;
				}

				firstTime = false;
			}
			Widgets.EndScrollView();
		}

		static Texture2D _checkboxBackground;
		static Texture2D CheckboxBackground
		{
			get
			{
				if (_checkboxBackground == null)
					_checkboxBackground = SolidColorMaterials.NewSolidColorTexture(new Color(0f, 0f, 0f, 0.5f));
				return _checkboxBackground;
			}
		}

		static string WaitingString
		{
			get
			{
				var i = (DateTime.Now.Ticks / 20) % 4;
				return new string[] { "....", "... .", ".. ..", ". ..." }[i];
			}
		}
	}

	internal class MiniDialog : Dialog_MessageBox
	{
		internal MiniDialog(string text, string buttonAText = null, Action buttonAAction = null, string buttonBText = null, Action buttonBAction = null, string title = null, bool buttonADestructive = false, Action acceptAction = null, Action cancelAction = null)
			: base(text, buttonAText, buttonAAction, buttonBText, buttonBAction, title, buttonADestructive, acceptAction, cancelAction) { }

		public override Vector2 InitialSize => new Vector2(320, 240);
	}
}