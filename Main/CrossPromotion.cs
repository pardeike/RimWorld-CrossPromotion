using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Brrainz
{
	public class CrossPromotion
	{
		static void Log(string message)
		{
			var fileOnDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CrossPromotion.log");
			File.AppendAllText(fileOnDesktop, message);
		}

		public static void Install(ulong userId)
		{
			Log($"INSTALL {DateTime.Now}");

			var mainAssembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
			Log($"mainAssembly {mainAssembly}");
			if (mainAssembly == null) return;

			var assemblyVersion = mainAssembly.GetName().Version;
			Log($"assemblyVersion {assemblyVersion}");
			var build = assemblyVersion.Build - 4805;
			Log($"build {build}");
			var version = new Version(assemblyVersion.Major, assemblyVersion.Minor, build);
			Log($"version {version}");
			var isSteamDeck = version >= new Version(1, 3, 3299);
			Log($"isSteamDeck {isSteamDeck}");
			var fileName = $"Brrainz.CrossPromotion{(isSteamDeck ? "SteamDeck" : "Steam")}.dll";
			var assembly = Assembly.GetExecutingAssembly();
			Log($"assembly {assembly}");
			using var resFilestream = assembly.GetManifestResourceStream(fileName);
			Log($"resFilestream {resFilestream}");
			if (resFilestream == null) return;

			var len = (int)resFilestream.Length;
			Log($"len {len}");
			var bytes = new byte[len];
			_ = resFilestream.Read(bytes, 0, len);
			Assembly loadedAssembly;
			try { loadedAssembly = Assembly.Load(bytes);
				Log($"loadedAssembly {loadedAssembly}");
			}
			catch (Exception ex) { Log($"ex {ex}"); return; }

			var t_CrossPromotion = loadedAssembly.GetType("Brrainz.CrossPromotion");
			Log($"t_CrossPromotion {t_CrossPromotion}");
			if (t_CrossPromotion == null) return;

			var m_Install = t_CrossPromotion.GetMethod("Install", BindingFlags.Static | BindingFlags.Public);
			Log($"m_Install {m_Install}");
			if (m_Install == null) return;

			_ = m_Install.Invoke(null, new object[] { userId });
			Log($"DONE");
		}
	}
}
