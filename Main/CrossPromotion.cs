using System;
using System.Linq;
using System.Reflection;

namespace Brrainz
{
	public class CrossPromotion
	{
		public static void Install(ulong userId)
		{
			var mainAssembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
			if (mainAssembly == null) return;

			var assemblyVersion = mainAssembly.GetName().Version;
			var build = assemblyVersion.Build - 4805;
			var version = new Version(assemblyVersion.Major, assemblyVersion.Minor, build);
			var isSteamDeck = version >= new Version(1, 3, 3299);
			var fileName = $"Brrainz.CrossPromotion{(isSteamDeck ? "SteamDeck" : "Steam")}.dll";
			var assembly = Assembly.GetExecutingAssembly();
			using var resFilestream = assembly.GetManifestResourceStream(fileName);
			if (resFilestream == null) return;

			var len = (int)resFilestream.Length;
			var bytes = new byte[len];
			_ = resFilestream.Read(bytes, 0, len);
			Assembly loadedAssembly;
			try { loadedAssembly = Assembly.Load(bytes); }
			catch (Exception) { return; }

			var t_CrossPromotion = loadedAssembly.GetType("Brrainz.CrossPromotion");
			if (t_CrossPromotion == null) return;

			var m_Install = t_CrossPromotion.GetMethod("Install", BindingFlags.Static | BindingFlags.Public);
			if (m_Install == null) return;

			_ = m_Install.Invoke(null, new object[] { userId });
		}
	}
}