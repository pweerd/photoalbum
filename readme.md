# Photoalbum website

## General

The album site consist of:

- a lightbox with your photo's
- a detail view of a photo, slideshow, etc
- a faces view where you can see the extracted faces and stamp them with a name.

It runs on premise from an Elasticsearch index, built by the photoalbum importer. This importer is available as a separate repo.
Currently it supports OCR-ring of the photos, caption generation and face recognition. All this information is searchable.



## Install & getting started

**Install** Elasticsearch.
If you want to use the bitmanager stemmer, please install version 7.4, since the bitmanager plugin is not yet available for later versions.
The needed plugin is downloadable from [](https://bitmanager.nl/es/7.4.0/).
If you choose to use a different version, the standard `index.config.js` will not work. Take a look at the `index_without_stemmer.config.js` file.

**Create** the `settings.xml` from the `settings.sample.xml`
Make sure you pay attention to 

- the external IP-address (if you want it to be treated as an internal address)
- the chosen root names (as specified in the import)
- the admin directory of the faces (again, same as in the import) 

**Run** the website and check for errors. It will probably return an empty page.

**Run** an import and the refresh the website. You should be able to see your photo's.

If you want to expose your site over the internet, you should use IIS or equivalent server.

#### Compilation and needed references

The project is created with VS2022. It is probably not possible to compile the project with earlier versions of VS.

Make sure you have a sub-folder '_references' in your solutions folder. This can be a junction to a common folder (see `@CreateRefsLink`) or a real folder.
All needed references can be found from **the zip that is supplied in the release**.



## Collections (users)

By default there is a main 'default' collection that is completely unfiltered. However, this collection is not available for external IP's by default. It is possible to add a collection of IP's that should be treated as internal, causing this default collection to be available for them.

if you wish to open up this collection for everyone, you can specify `expose="."` (it is a regex).

Collections are implemented as a simple Elasticsearch filter. See the `settings.sample.xml` for an example.



## Possible queries

Take a look at the settings.sample.xml, the search nodes. Here you can specify the possible sort options and the possible queries. 

Query syntax is basically boolean.

Operators: AND, OR, NOT. Case-sensitive, in case of no operator the default is AND.

Parenthesis: () are supported

Range-queries: like [INCLUSIVE_FROM**..**EXCLUSIVE_UNTIL>, example everything from 2023: `date:[2023..]`

Phrase queries by using " or '

Wildcard queries by using a *

Field queries like `field:value` or `field:(value1 OR value2)`



## Logging

The website logs via the Bitmanager rep-around logger, or the trace logger if no logger is found. The logger and viewer can downloaded from [](https://bitmanager.nl/distrib/) (please use the V2 version).



## Faces page

In order to use face recognition you need to manually assign names to some extracted faces and redo the automatic face assignment afterwards. To support this process there is a `/faces` page. This page is only available for internal users! In this page you can see all extracted faces. Queries that you do there will be searching through the file names. 

You need to create a facenames.txt file. At the moment this file contains 1 name per line. After creation you can refresh the `/faces` page and you should see you face names.

Clicking on a face name will select that name and you can 'stamp' the name on multiple faces. First time its recommended to select a few (say 5) faces per name and re-run the face assignment step. After this step finishes you can stamp faces that are wrong assigned or names that matched but with a very low score. In the end you will probably have some 10-20 faces assigned per name.

After running the last step of the import (the photo's step) the assigned face names are merged into the main database. The import emits the face names as general text into the main field, but names can be searched by a specific `name:"some name"` query. Also notable is the query `faces:1`. This query selects all photo's with 1 recognized face.

if you want to see photo's with 2 people together you can use `name:("person1" "person2")`



## IP class

For each request an IP-class is determined by inspecting the remote IP. Typically used classes are:

- internal
  For all non-routable addresses like 127.0.0.x, 192.168.x.x, etc
- internal_home
  For the external IP of the server.
- external
  For all other addresses
- authenticated
  For authenticated users

For more information, see the `settings.xml`.



## Authentication

The website supports simple win32 authentication via the `/login` page.

After authentication you are treated typically as an internal user. So you can get detailed error information, you can see hidden photo's, internal-only collection can be available (if they allow for the authenticated IP-class).



## Show hidden photos

There are 2 hiding modes:

- only show hidden photo's when accessed from the LAN
  Photo's are hidden if the filename part start with or ends with an underscore.
  These photo's are only visible from with the LAN (or of you are authenticated)
-  only show hidden photo's when accessed from the LAN and explicitly asked for.
  Photo's can be hidden by setting this in the importsettings in a directory. If that is the case, the photo's are only shown when the url of the website contains `hide=false` and the website is accessed from the local LAN.



