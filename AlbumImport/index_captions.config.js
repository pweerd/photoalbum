{
   "settings": {
      "number_of_shards" : 1,
      "number_of_replicas" : 0,
      "refresh_interval": "30s",
   },

   "mappings": {
      "_meta": { "lastmod": "" },
      "dynamic": false,
      "properties": {
         "ts": {"type": "date"},
         "text_en": {"type": "text"},
         "text_nl": {"type": "text"}
      }
   }
}