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
         "date": {"type": "date"},
         "id": {"type": "text", "index": true,"fields": {
            "keyword": {"ignore_above": 256, "type": "keyword"}
         }},
         "root": {"type": "keyword", "index": true},
         "user": {"type": "keyword", "index": true}
      }
   }
}