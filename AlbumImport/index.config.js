{
   "settings": {
      "number_of_shards" : 1,
      "number_of_replicas" : 0,
      "refresh_interval": "60s",
      "analysis" : {
         "char_filter" : {
            "html_strip" : {
               "type" : "html_strip",
               "read_ahead" : 1024
            },
            "prepare_delimiter": {
               "type": "mapping",
               "mappings": ["-=>_", "\\uFFFD=>_"]
            },
            "repl_dot": {
               "type": "mapping",
               "mappings": [".=>\\u0020", "\\uFFFD=>\\u0020"]
            }
         },
         "tokenizer": {
            "non_alphanum": {
               "type": "pattern",
               "pattern": "(?:^|\\s)[^\\p{L}\\p{N}]*|[^\\p{L}\\p{N}]*(?:\\s|$)"
            }
         },
         "filter" : {
            "shingles": {
               "type": "shingle",
               "min_shingle_size": 2,
               "max_shingle_size": 2,
               "token_separator": ""
            },
            "stem_nl": {
              "type": "snowball",
              "language": "dutch",
              "emit_original": "false"
            },
            "index_delimiter": {
              "type": "word_delimiter",
              "split_on_numerics": true,
              "catenate_words": true,
              "catenate_numbers": true,
              "preserve_original": false,
              "adjust_offsets": true
            },
            "search_delimiter": {
              "type": "word_delimiter_graph",
              "split_on_numerics": true,
              "generate_number_parts": false,
              "catenate_words": true,
              "catenate_numbers": true,
              "preserve_original": false,
              "adjust_offsets": true
            },
            "syn_repl": {
              "type": "synonym",
              "synonyms": [
              ]
            },

            "syn_add": {
               "type": "synonym",
               "synonyms": [
               ]
            }
          },
          "analyzer" : {
            "lc_text" : {
               "tokenizer" : "non_alphanum",
               "filter": ["asciifolding", "lowercase", "index_delimiter"]
             },
             "lc_text_shingles" : {
                "tokenizer" : "non_alphanum",
                "filter": ["asciifolding", "lowercase", "shingles"]
             },
             "lc_text_index" : {
                "tokenizer" : "non_alphanum",
                "filter": ["asciifolding", "lowercase", "syn_repl", "syn_add", "index_delimiter"]
             },
             "nl_stem_index" : {
                "tokenizer" : "non_alphanum",
                "filter": ["asciifolding", "lowercase", "syn_repl", "syn_add", "index_delimiter", "stem_nl"]
             },
             "lc_text_search" : {
                "tokenizer" : "non_alphanum",
                "filter": ["asciifolding", "lowercase", "syn_repl", "search_delimiter"]
             },
             "nl_stem_search" : {
                "tokenizer" : "non_alphanum",
                "filter": ["asciifolding", "lowercase", "syn_repl", "search_delimiter", "stem_nl"]
             }
          },
          "normalizer" : {
            "lc_facet" : {
               "type" : "custom",
                  "filter": ["asciifolding", "lowercase"]
            }
         }

      }
   },

   "mappings": {
      "_meta": { "lastmod": "" },
      "_source": { "excludes": ["_text", "_type"] },
      "dynamic": false,
      "properties": {
         "file": { "type": "text", "analyzer": "lc_text", "copy_to": ["all", "all_s"] },
         "date": {"type": "date"},
         "year": {"type": "integer"},
         "month": {"type": "integer"},
         "day": { "type": "integer" },
         "sort_key": { "type": "long", "doc_values": true },
         "hide": { "type": "keyword", "normalizer": "lc_facet" },
         "album": {
            "type": "text", "analyzer": "lc_text_index", "search_analyzer": "lc_text_search", "copy_to": ["all", "all_s"],
               "fields": {
               "facet": { "type": "keyword", "normalizer": "lc_facet", "doc_values": true }
            }
         },
         "type": { "type": "keyword", "normalizer": "lc_facet", "doc_values": false },
         "mime": { "type": "keyword", "normalizer": "lc_facet", "doc_values": false },
         "c_name": { "type": "keyword", "normalizer": "lc_facet", "doc_values": true },
         "c_id": { "type": "keyword", "normalizer": "lc_facet", "doc_values": true },
         "season": { "type": "keyword", "normalizer": "lc_facet", "doc_values": false },
         "album_len": { "type": "integer", "doc_values": true },
         "duration": { "type": "integer", "doc_values": true },
         "camera": { "type": "text", "analyzer": "lc_text_index", "search_analyzer": "lc_text_search" },
         "tz": { "type": "text", "analyzer": "lc_text"},
         "location": { "type": "geo_point" },
         "extra_location": { "type": "text", "analyzer": "lc_text_shingles", "search_analyzer": "lc_text", "copy_to": ["all", "all_s"] },
         "cc": { "type": "keyword", "normalizer": "lc_facet" },
         "ocr": { "type": "text", "analyzer": "lc_text_index", "search_analyzer": "lc_text_search", "copy_to": ["text_nl", "text_nl_s", "all", "all_s"] },
         "text": { "type": "text", "analyzer": "lc_text_index", "search_analyzer": "lc_text_search", "copy_to": ["text_nl", "text_nl_s", "all", "all_s"] },
         "all": { "type": "text", "analyzer": "lc_text_index", "search_analyzer": "lc_text_search" },
         "all_s": { "type": "text", "analyzer": "nl_stem_index", "search_analyzer": "nl_stem_search" },
         "text_en": { "type": "text", "analyzer": "lc_text_index", "search_analyzer": "lc_text_search" },
         "text_nl": { "type": "text", "analyzer": "lc_text_index", "search_analyzer": "lc_text_search", "copy_to": ["text_nl_s","all", "all_s"] },
         "text_nl_s": { "type": "text", "analyzer": "nl_stem_index", "search_analyzer": "nl_stem_search" },
         "tags": { "type": "keyword", "normalizer": "lc_facet" },
         "extra": { "type": "keyword", "normalizer": "lc_facet" },
         "root": { "type": "keyword", "normalizer": "lc_facet" },
         "user": { "type": "keyword", "normalizer": "lc_facet" },
         "yyyymmdd": { "type": "keyword" },
         "ext": { "type": "keyword", "normalizer": "lc_facet" },
         "orientation": { "type": "text", "analyzer": "lc_text" },
         "height": { "type": "integer", "index": false },
         "width": { "type": "integer", "index": false },
         "ele": { "type": "integer", "index": false },
         "face_count": { "type": "integer" },
         "names": {"type": "nested", "properties": {
            "name": {"type": "text", "analyzer": "lc_text", "copy_to":["all", "all_s"], "fields": {
               "facet": { "type": "keyword", "normalizer": "lc_facet", "doc_values": true }
            }},
            "id": {"type": "integer"},
            "match_score": {"type": "float"},
            "face_detect_score": {"type": "float"},
            "detected_face_detect_score": {"type": "float"},
            "score_all": {"type": "float"}
         }}

      }
   }
}