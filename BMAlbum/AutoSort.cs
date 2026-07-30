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

      private readonly SortMode SortForOneAlbum;
      private readonly SortMode SortForLocationOrNameQuery;
      private readonly SortMode SortForFuzzyQuery;
      private readonly SortMode SortForOtherQuery;
      private readonly SortMode SortForNoQuery;
      private readonly SortMode SortForPortraitQuery;
      public AutoSort (XmlNode node, SortModes sortModes) {
         try {
            SortForPortraitQuery = sortModes.Find ("oldontop", true);
            if (node == null) {
               SortForPortraitQuery = SortForOneAlbum = sortModes.Find ("oldontop", true);
               SortForLocationOrNameQuery = sortModes.Find ("relevance", true);
               SortForFuzzyQuery = SortForLocationOrNameQuery;
               SortForOtherQuery = SortForLocationOrNameQuery;
               SortForNoQuery = sortModes.Find ("newontop", true);
            } else {
               SortForOneAlbum = readSortMode (node, "@album", null, sortModes);
               SortForPortraitQuery = readSortMode (node, "@portrait_query", null, sortModes);
               SortForLocationOrNameQuery = readSortMode (node, "@loc_or_name_query", null, sortModes);
               SortForFuzzyQuery = readSortMode (node, "@fuzzy_query", null, sortModes);
               SortForOtherQuery = readSortMode (node, "@other_query", null, sortModes);
               SortForNoQuery = readSortMode (node, "@default", null, sortModes);
            }
         } catch (Exception e) {
            throw new BMNodeException (e, node, e.Message);
         }
      }

      public SortMode ResolveSortMode (List<ParserValueNode> query, bool oneAlbum, bool fuzzy) {
         if (oneAlbum) return SortForOneAlbum;
         if (fuzzy) return SortForFuzzyQuery;
         if (query == null || query.Count == 0) return SortForNoQuery;

         for (int i = 0; i < query.Count; i++) {
            var processed = query[i].ProcessedBy;
            if (processed is PortraitSearcher)
               return SortForPortraitQuery;
            if (processed is LocationSearcher || processed is NameSearcher || processed is PinSearcher)
               return SortForLocationOrNameQuery;
         }
         return SortForOtherQuery;
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
