using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Aether
{
	public static class Utility
	{
		public static class Layers
		{
			public static LayerMask IgnoreRaycast = LayerMask.NameToLayer("Ignore Raycast");

			public static LayerMask DroppedItem = LayerMask.NameToLayer("DroppedItem");
		}

		public static readonly string[] FilesizeNames = new string[5] { "B", "KB", "MB", "GB", "TB" };

		public static readonly string[] CountNames = new string[5] { "", "K", "M", "B", "T" };

		public static int FrametimeIntervals(float interval, float time, float delta)
		{
			return (int)(math.floor(time / interval) - math.floor((time - delta) / interval));
		}

		public static bool AtInterval(float interval)
		{
			return FrametimeIntervals(interval, Time.deltaTime, Time.time) != 0;
		}

		public static bool Toggle(this ref bool val)
		{
			val = !val;
			return val;
		}

		public static float Rand()
		{
			return UnityEngine.Random.value;
		}

		public static int RandInt(int min, int max)
		{
			return UnityEngine.Random.Range(min, max);
		}

		public static void Assert(bool isTrue, Func<string> evalMessage = null)
		{
		}

		public static void ForVolume(int3 min, int3 max, Action<int3> visitor)
		{
			for (int i = min.x; i < max.x; i++)
			{
				for (int j = min.y; j < max.y; j++)
				{
					for (int k = min.z; k < max.z; k++)
					{
						visitor(new int3(i, j, k));
					}
				}
			}
		}

		public static void ForVolumeMid(int3 mid, int radiusInclusive, Action<int3> visitor)
		{
			ForVolume(mid - radiusInclusive, mid + radiusInclusive + 1, visitor);
		}

		public static void ForVolumeMid(int radiusInclusive, Action<int3> visitor)
		{
			ForVolume(-radiusInclusive, radiusInclusive + 1, visitor);
		}

		public static void ForVolumeSpread(int nXZ, int nY, Action<int3> visitor)
		{
			int num = math.max(nXZ, nY);
			for (int i = 0; i < num; i++)
			{
				for (int j = -i; j <= i; j++)
				{
					for (int k = -i; k <= i; k++)
					{
						for (int l = -i; l <= i; l++)
						{
							if ((math.abs(l) >= i || math.abs(j) >= i || math.abs(k) >= i) && math.abs(l) <= nXZ && math.abs(j) <= nY && math.abs(k) <= nXZ)
							{
								visitor(new int3(l, j, k));
							}
						}
					}
				}
			}
		}

		public static bool CursorRay(out Ray ray)
		{
			ray = default(Ray);
			if (!float.IsFinite(Input.mousePosition.x))
			{
				return false;
			}
			ray = Camera.main.ScreenPointToRay(Input.mousePosition);
			return true;
		}

		public static string GetLogFilePath()
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Low", Application.companyName, Application.productName, "Player.log");
		}

		public static string StrTimeDuration(int sec, string txHr = ":", string txMin = ":", string txSec = "", bool forceUnits = false)
		{
			StringBuilder stringBuilder = new StringBuilder($"{sec % 60:00}{txSec}");
			int num = sec / 60 % 60;
			if (num > 0 || forceUnits)
			{
				stringBuilder.Insert(0, $"{num:00}{txMin}");
			}
			int num2 = sec / 60 / 60;
			if (num2 > 0 || forceUnits)
			{
				stringBuilder.Insert(0, $"{num2:00}{txHr}");
			}
			return stringBuilder.ToString();
		}

		public static string StrDayTime(float daytime, bool hr12 = true)
		{
			float num = (daytime * 24f + 6f) % 24f;
			float x = math.frac(num) * 60f;
			float x2 = math.frac(x) * 60f;
			return string.Format("{0:00}:{1:00}:{2:00}{3}", math.floor(hr12 ? (num % 13f) : num), math.floor(x), math.floor(x2), (!hr12) ? "" : ((num < 13f) ? " AM" : " PM"));
		}

		public static int NumUnit(float num, float unitLen, out float unitedNum)
		{
			unitedNum = num;
			int num2 = 0;
			while (unitedNum >= unitLen)
			{
				num2++;
				unitedNum /= unitLen;
			}
			return num2;
		}

		public static string StrFilesize(float numBytes)
		{
			float unitedNum;
			int idx = NumUnit(numBytes, 1024f, out unitedNum);
			return $"{unitedNum:0.00} {FilesizeNames.GetClamped(idx)}";
		}

		public static string StrCountUnited(float count)
		{
			if (count < 1000f)
			{
				return count.ToString(CultureInfo.InvariantCulture);
			}
			float unitedNum;
			int idx = NumUnit(count, 1000f, out unitedNum);
			return $"{unitedNum:0.##} {CountNames.GetClamped(idx)}";
		}

		public static string ToSimpleString(this Vector3 v)
		{
			return $"{v.x:0.##}, {v.y:0.##}, {v.z:0.##}";
		}

		public static void Increase<K>(this Dictionary<K, float> dict, K key, float add)
		{
			if (dict.TryGetValue(key, out var value))
			{
				dict[key] = value + add;
			}
			else
			{
				dict.Add(key, add);
			}
		}

		public static int RemoveAll<K, V>(this Dictionary<K, V> dict, IEnumerable<K> removes)
		{
			int num = 0;
			foreach (K remove in removes)
			{
				if (dict.Remove(remove))
				{
					num++;
				}
			}
			return num;
		}

		public static int RemoveAll<K>(this HashSet<K> set, IEnumerable<K> removes)
		{
			int num = 0;
			foreach (K remove in removes)
			{
				if (set.Remove(remove))
				{
					num++;
				}
			}
			return num;
		}

		public static bool TryRemoveLast<T>(this List<T> ls)
		{
			if (ls.IsEmpty())
			{
				return false;
			}
			ls.RemoveAt(ls.Count - 1);
			return true;
		}

		public static int RemoveIf<T>(this List<T> ls, Func<T, bool> predict)
		{
			int num = 0;
			int num2 = 0;
			while (num2 < ls.Count)
			{
				if (predict(ls[num2]))
				{
					ls.RemoveAt(num2);
					num++;
				}
				else
				{
					num2++;
				}
			}
			return num;
		}

		public static T LastOr<T>(this List<T> ls, T orDefault)
		{
			if (ls.Count == 0)
			{
				return orDefault;
			}
			return ls[ls.Count - 1];
		}

		public static T GetClamped<T>(this T[] arr, int idx)
		{
			return arr[Mathf.Clamp(idx, 0, arr.Length - 1)];
		}

		public static T GetRandom<T>(this T[] ls)
		{
			return ls[UnityEngine.Random.Range(0, ls.Length - 1)];
		}

		public static bool IsEmpty<T>(this List<T> ls)
		{
			return ls.Count == 0;
		}

		public static int[] Sequence(int n, int begin = 0)
		{
			int[] array = new int[n];
			for (int i = 0; i < n; i++)
			{
				array[i] = i + begin;
			}
			return array;
		}

		public static void ForChildren(this Transform obj, Action<Transform> visitor)
		{
			for (int i = 0; i < obj.childCount; i++)
			{
				visitor(obj.GetChild(i));
			}
		}

		public static void ForChildrenRev(this Transform obj, Action<Transform> visitor)
		{
			for (int num = obj.childCount - 1; num >= 0; num--)
			{
				visitor(obj.GetChild(num));
			}
		}

		public static void DestroyChildren(this Transform obj, bool immediately = false, Func<Transform, bool> delIf = null, bool includeSelf = false)
		{
			obj.ForChildrenRev(delegate(Transform e)
			{
				if (delIf == null || delIf(e))
				{
					if (immediately)
					{
						UnityEngine.Object.DestroyImmediate(e.gameObject);
					}
					else
					{
						UnityEngine.Object.Destroy(e.gameObject);
					}
				}
			});
			if (includeSelf)
			{
				if (immediately)
				{
					UnityEngine.Object.DestroyImmediate(obj.gameObject);
				}
				else
				{
					UnityEngine.Object.Destroy(obj.gameObject);
				}
			}
		}

		public static void DestroyAll<T>(this IEnumerable<T> ls) where T : Component
		{
			foreach (T l in ls)
			{
				UnityEngine.Object.Destroy(l);
			}
		}

		public static T GetOrAddComponent<T>(this GameObject obj) where T : Component
		{
			T val = obj.GetComponent<T>();
			if (!val)
			{
				val = obj.AddComponent<T>();
			}
			return val;
		}

		public static T GetOrAddComponent<T>(this Transform obj) where T : Component
		{
			return obj.gameObject.GetOrAddComponent<T>();
		}

		public static void ToggleActive(this GameObject obj)
		{
			obj.SetActive(obj.activeSelf);
		}

		public static void LockCursor(bool lockCursor)
		{
			Cursor.lockState = (lockCursor ? CursorLockMode.Locked : CursorLockMode.None);
		}

		public static RectTransform AsRect(this Transform transform)
		{
			return transform as RectTransform;
		}

		public static void SetUiSize(Transform rect, float size, float sizeY = -1f)
		{
			SetUiSize(rect as RectTransform, size, sizeY);
		}

		public static void SetUiSize(RectTransform rect, float size, float sizeY = -1f)
		{
			rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size);
			rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, (sizeY >= 0f) ? sizeY : size);
		}

		public static void SetButtonNormalColor(this Button btn, Color color)
		{
			ColorBlock colors = btn.colors;
			colors.normalColor = color;
			btn.colors = colors;
		}

	}
}
