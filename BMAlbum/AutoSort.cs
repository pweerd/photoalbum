using Bitmanager.BoolParser;
using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.Query;
using Bitmanager.Xml;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using System.Xml;

namespace BMAlbum {

   //         <auto_sort album="oldontop" query="relevance|newontop" pin="relevance" default="newontop"/>

   public class AutoSort {

      private readonly SortMode Album;
      private readonly SortMode LocationOrNameQuery;
      private readonly SortMode FuzzyQuery;
      private readonly SortMode OtherQuery;
      private readonly SortMode Default;
      public AutoSort (XmlNode node, SortModes sortModes) {
         try {
            if (node == null) {
               Album = sortModes.Find ("oldontop", true);
               LocationOrNameQuery = sortModes.Find ("relevance", true);
               FuzzyQuery = LocationOrNameQuery;
               OtherQuery = LocationOrNameQuery;
               Default = sortModes.Find ("newontop", true);
            } else {
               Album = readSortMode (node, "@album", null, sortModes);
               LocationOrNameQuery = readSortMode (node, "@loc_or_name_query", null, sortModes);
               FuzzyQuery = readSortMode (node, "@fuzzy_query", null, sortModes);
               OtherQuery = readSortMode (node, "@other_query", null, sortModes);
               Default = readSortMode (node, "@default", null, sortModes);
            }
         } catch (Exception e) {
            throw new BMNodeException (e, node, e.Message);
         }
      }

      public SortMode ResolveSortMode (List<ParserValueNode> query, bool oneAlbum, bool fuzzy) {
         if (oneAlbum) return Album;
         if (fuzzy) return FuzzyQuery;
         if (query != null && query.Count > 0) {
            for (int i = 0; i < query.Count; i++) {
               var processed = query[i].ProcessedBy;
               if (processed is LocationSearcher || processed is NameSearcher || processed is PinSearcher)
                  return LocationOrNameQuery;
            }
            return OtherQuery;
         }
         return Default;
      }

      private SortMode readSortMode (XmlNode node, string key, string def, SortModes sortModes) {
         var txtArr = node.ReadStr (key).SplitStandard ();
         var sortArr = new SortMode[txtArr.Length];
         for (int i = 0; i < txtArr.Length; i++) {
            if (string.Equals ("auto", txtArr[i], StringComparison.InvariantCultureIgnoreCase))
               throw new Exception ("Auto sortmode cannot be resolved to 'auto'.");
            sortArr[i] = sortModes.Find (txtArr[i], true);
         }
         switch (sortArr.Length) {
            case 0: throw new Exception ("No sortmodes found");
            case 1: return sortArr[0];
         }

         //found concatenated modes
         var ret = new SortMode (sortArr[0].Name, sortArr[0].Text, "dummy", ESSortDirection.desc, null);
         ret.SortElements.Clear ();
         for (int i = 0; i < sortArr.Length; i++) {
            ret.SortElements.AddRange (sortArr[i].SortElements);
         }
         return ret;
      }
   }
}
