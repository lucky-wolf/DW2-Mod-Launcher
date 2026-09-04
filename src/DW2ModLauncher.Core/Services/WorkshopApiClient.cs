using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Calls Steam's public GetPublishedFileDetails API to learn each subscribed Workshop item's
    /// last-updated time, title, description and other display metadata.
    /// </summary>
    public static class WorkshopApiClient
    {
        public static Dictionary<string, long> FetchRemoteTimes(List<string> ids, out Dictionary<string, WorkshopRemoteDetail> remoteDetails)
        {
            Dictionary<string, long> result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            remoteDetails = new Dictionary<string, WorkshopRemoteDetail>(StringComparer.OrdinalIgnoreCase);
            if (ids == null || ids.Count == 0) return result;
            const int batchSize = 50;
            for (int start = 0; start < ids.Count; start += batchSize)
            {
                List<string> batch = ids.Skip(start).Take(batchSize).ToList();
                StringBuilder form = new StringBuilder();
                form.Append("itemcount=").Append(batch.Count);
                for (int i = 0; i < batch.Count; i++)
                    form.Append("&publishedfileids%5B").Append(i).Append("%5D=").Append(Uri.EscapeDataString(batch[i]));
                byte[] body = Encoding.UTF8.GetBytes(form.ToString());

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/");
                req.Method = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.ContentLength = body.Length;
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.UserAgent = "DW2ModLauncherBeta/0.4.0";
                using (Stream stream = req.GetRequestStream()) stream.Write(body, 0, body.Length);
                string responseText;
                using (HttpWebResponse response = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) responseText = reader.ReadToEnd();

                object rootObj = LooseJson.Parse(responseText);
                Dictionary<string, object> root = rootObj as Dictionary<string, object>;
                if (root == null || !root.ContainsKey("response")) continue;
                Dictionary<string, object> responseObj = root["response"] as Dictionary<string, object>;
                if (responseObj == null || !responseObj.ContainsKey("publishedfiledetails")) continue;
                object[] details = responseObj["publishedfiledetails"] as object[];
                if (details == null) continue;
                foreach (object itemObj in details)
                {
                    Dictionary<string, object> item = itemObj as Dictionary<string, object>;
                    if (item == null) continue;
                    string id = DictionaryValue(item, "publishedfileid");
                    string time = DictionaryValue(item, "time_updated");
                    long value = 0;
                    if (!string.IsNullOrWhiteSpace(id) && long.TryParse(time, out value)) result[id] = value;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        WorkshopRemoteDetail detail = new WorkshopRemoteDetail();
                        detail.Id = id;
                        detail.Title = DictionaryValue(item, "title");
                        detail.Description = DictionaryValue(item, "description");
                        detail.PreviewUrl = DictionaryValue(item, "preview_url");
                        detail.Creator = DictionaryValue(item, "creator");
                        detail.TimeUpdated = value;
                        long created;
                        long.TryParse(DictionaryValue(item, "time_created"), out created);
                        detail.TimeCreated = created;
                        detail.Tags = WorkshopTagsValue(item);
                        long size;
                        long.TryParse(DictionaryValue(item, "file_size"), out size);
                        detail.FileSize = size;
                        remoteDetails[id] = detail;
                    }
                }
            }
            return result;
        }

        private static string DictionaryValue(Dictionary<string, object> d, string key)
        {
            if (d == null) return "";
            foreach (KeyValuePair<string, object> kv in d)
                if (!string.IsNullOrEmpty(kv.Key) && kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && kv.Value != null) return Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            return "";
        }

        private static string WorkshopTagsValue(Dictionary<string, object> item)
        {
            if (item == null || !item.ContainsKey("tags")) return "";
            List<string> tags = new List<string>();
            object[] array = item["tags"] as object[];
            if (array != null)
                foreach (object value in array)
                {
                    Dictionary<string, object> tag = value as Dictionary<string, object>;
                    string text = DictionaryValue(tag, "tag");
                    if (!string.IsNullOrWhiteSpace(text)) tags.Add(text);
                }
            return string.Join(", ", tags.ToArray());
        }
    }
}
