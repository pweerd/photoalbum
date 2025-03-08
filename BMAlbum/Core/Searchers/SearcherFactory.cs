/*
 * Copyright © 2024, De Bitmanager
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *    http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Bitmanager.Query;
using System.Xml;


namespace BMAlbum {
   public class SearcherFactory : ISearcherFactory {
      public static readonly SearcherFactory Instance = new SearcherFactory();
      private SearcherFactory () { }
      public ISearcher CreateSearcher (XmlNode node, string type, string field, string searchField, SearchFieldConfig cfg) {
         if (type == "name_searcher") return new NameSearcher (field, searchField, cfg);
         if (type == "pin_searcher") return new PinSearcher (node, field, searchField, cfg);
         return null;
      }
   }
}
