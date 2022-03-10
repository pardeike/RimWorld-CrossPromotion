using System.Reflection;

namespace Brrainz
{
	public class CrossPromotion
	{
		public static void Install(ulong userId)
		{
			var fileName = "Brrainz.CrossPromotionSteam.dll";
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
