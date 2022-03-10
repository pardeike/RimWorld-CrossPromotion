using System;
using System.Reflection;

namespace Brrainz
{
	public class CrossPromotion
	{
		public static void Install(ulong userId)
		{
			var t_VersionControl = Type.GetType("RimWorld.VersionControl");
			var version = (Version)t_VersionControl.GetProperty("CurrentVersion").GetValue(null);
			var isSteamDeck = version >= new Version(1, 3, 3299);
			var fileName = $"Brrainz.CrossPromotion{(isSteamDeck ? "SteamDeck" : "Steam")}.dll";
			var assembly = Assembly.GetExecutingAssembly();
			using var resFilestream = assembly.GetManifestResourceStream(fileName);
			var len = (int)resFilestream.Length;
			var bytes = new byte[len];
			_ = resFilestream.Read(bytes, 0, len);
			var loadedAssembly = Assembly.Load(bytes);
			var t_CrossPromotion = loadedAssembly.GetType("Brrainz.CrossPromotion");
			var m_Install = t_CrossPromotion.GetMethod("Install", BindingFlags.Static | BindingFlags.Public);
			_ = m_Install.Invoke(null, new object[] { userId });
		}
	}
}
