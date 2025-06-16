---
title: Getting started | Album
---
[toc]

# Photoalbum website/indexer

In the following text "photo's" means "photo's or video's".




## Installer

The installation program will install the following components:

- Core components
  This is the logging infrastructure and a viewer for the logs (and Windows eventlogs)
- Album website
  The actual website that shows your photo's
- Import engine
  The engine that imports all the photo's. Several steps will generate additional data for your photo's.
- Elasticsearch
  Elasticsearch is the index-engine that is used to index your photo's.
- Python
  Python is needed for running the AI networks in order to generate captions.
- FFMpeg and Exiftool
  These tools are needed to convert video's into a supported format, extract frames from a video and modify metadata in a photo/video.
- A desktop/startmenu folder "`bitmanager`" with links to the webserver, website and importer.


The installer can host your website in IIS (only possible in Windows Pro versions) or via the builtin webserver. Choose IIS if you plan to

- publish your album website over the local network or even the internet

- automatic start the webserver

If you don't want hosting via IIS, you need to start the webserver first (see the link in the bitmanager-folder). 

It is possible to automatic start the webserver by copying this link to windows startup map (*C:\Users\Username\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup*) or to schedule it at system start by using the task-scheduler.



## Getting started

### Just index your photo's

After installing all the components, it is best to simply index your photo's first. Without and additional steps. This is the fastest way to get you a running website where you can see how your photo-collection is indexed.

To do so, you need to start the index engine (the installer created a shortcut in the bitmanager directory on your desktop).

Select only these 2 datasources:

1. ids
   Create a list of all your photo's, to be used in the other steps
2. photos
   Creates an elasticsearch index from your photo's. This index will contain all your photo's, enriched with data from the other datasources, that we will skip for now.

Then click "`import`".
After a minute or so (depending on your collection and computer speed) the import should be finished. You can take a look in the website to view the result.



### Users / more than 1 location

It is possible to index photo's for multiple users. If that is the case, you have to check the import.xml and add lines like

```
 <provider type="FileStreamDirectory" virtualroot="E" user="alles" root="e:\fotos" sort="filename|asc" recursive="true">
 </provider>
```

As you can see you can specify a file location  and a user. If you add file locations, you need to specify a different `virtualroot` and also update the `settings.xml` in the website for these roots.
Of course you can also add multiple locations for 1 user (if your photo's are spreaded over different locations)

If your photo's are in 1 location and you only have 1 user, you don't need to do anything.



### Check the album names and dates

Album names are determined by inspecting the filename and or the directory-name.

The indexer supports 2 types of photo organization:

- Use a directory to contain all photo's about a subject

- Use the subject in the filename.
  For instance "`2003-04-05 Holiday-001.jpg`" and "`2003-04-05 Holiday-002.jpg`".
  In this case the extracted album-name will be "Holiday".

 A default organization can be specified in the `import.xml` of the indexer, but it can also be specified by composing a file named `importsettings.xml` and put that in your collection. These settings are typically inherited, so if you put this file in the root of your collection, its is active until some other `importsettings.xml` is encountered deeper in your collection. This ensures that it is possible to have different organizations in parts of your collection.

Dates are extracted from the metadata of your photo's, but it can also be extracted from the filename or directory name. You can customize this behavior in the `importsettings.xml`. For instance, if you have a directory with scanned images and the scanned has saved the date when the photo was scanned, it makes sense to put dates in the file- or directory-name and disallow dates to be taken from the metadata for that part of the collection.

After customizing or adding items to the collection you can repeat selecting the "`ids`" and "`photos`" datasources and import again, until the index is what you want.



### Run (one of) the other datasources

First of all, leave the flags as is. Only choose the datasource to run. Every datasource below can be run standalone. Meaning: if the datasource hasn't been run or is only partially run, the photos datasource will only pickup the information that is available. As a consequence, you always need to run the photos datasource to activate updated information from the others.

##### videos

The videos datasource is responsible for converting existing video's into a MP4/H265 format and to extract a frame from the video to be shown in the website. This frame-image is also used by the other datasources.

Video-conversions are very time consuming. You can check progress by looking in the logs for the importer (import.bmlg).


##### ocr

This step tries to extract readable text from a photo. Most photo's don't contain text, but sometime it does. Like some photo's from Whatsapp, or photo's from a tombstone or a sign along the road.

This step is time consuming and might take days. The step is artificially pausing after each photo in order to prevent your PC from overheating and to keep the PC responsive. It should be possible to keep working on the same PC while running this step.

The step is restartable, so you can cancel the import and resume later.


##### captions

Captions are generated by running the photo's through a vision AI network. It tries to describe the photo in natural language (English). The text will be translated into Dutch by a call to google translate.

This step is time consuming and might take days. The step is artificially pausing after each photo in order to prevent your PC from overheating and to keep the PC responsive. It should be possible to keep working on the same PC while running this step.

The step is restartable, so you can cancel the import and resume later.


##### face_extract

This step extracts faces from each photo. The face itself and a fingerprint will be stored in a storage file to be used later. The fingerprint will be used in the next face_match step, the photo will be used in the website's faces view. 


##### face_match

Before running the face-matching step, compose a text-file with all the people you know and expect to occur in your photo's. This file is named `FaceNames.txt` and located in the `albumimport` directory.
Note that once the face_match step has run, you cannot reorder names in this file, since only the name-index from this file is used to match a facename. It ***is*** possible however to append names to the file.

Once you have this file, open the faces view of the album site. You can reach this by

- using the context menu of a photo: "`ga naar gezichten`"

- change mode to faces, like: `/album/alles/?mode=faces`

In the faces view, you can select a name from the left pane, and the stamp it on some faces. Note that you can use simple searches/albums/years/sorting to wade through your collection.

Try to stamp a few (+/- 10) different faces per person. 

Now run the face_match and the photo's step. This should finish pretty fast, after which you can see the automatic assigned name to a face. 

For a manual about the possible searches and actions in the face view, click on the questionmark in the searchbox (only shown if the searchbox is empty).

It will be tempting to correct mistakes in the automatic face recognition. But know that it will be never 100% OK. It will keep making mistakes, especially for faces that are in side-view or blurred. You should look at it as only one of the tools for searching through your collection.




### Hide photo's

You might want to hide photo's. The are 2 hide modes:

- hidden
  These photo's have a name that start or end with an '\_'. Like "something-private\_.jpg" or "\_something-private.jpg". It is also possible to specify `hide=external` in the `importsettings.xml` located in the directory tree of you photo collection.
  If you view the album from the local PC or the local network, these photo's are shown, but if you view the album over the internet, the photo's are excluded.

- super hidden 
  These photo's are never shown. Not local, not over the internet.
  If you still want to view these photo's, you need to access the site from the local PC and append "`&unhide`" to the url in the browser. 
  Super-hidden photo's can be specified only via an `importsettings.xml`, by setting "`hide=always`".

After changing hide-settings, you need to run the photos datasource again.




### Creating subsets

The album website supports subsets of photo's. For instance it is possible to have multiple users and make a subset for every user.
It is also possible to create a subset for an event. Like making a subset that contains all new years eve photo's. Or if you organize reunions in you family, you can create a subset "family" that contains photo's for all these events. 

A subset is created by customizing the `settings.xml` in the website. A subset has a name and one or more queries. Such a query searches for the photo's that will be selected in the website. See the `import.xml` for an example. Changing the `import.xml` has an immediate effect on the website.

Sometimes it is difficult to come up with a simple query to select photo's for a subset. In that case it might be handy to tag photo's with one or more tags (terms). These tags are specified in the `import.xml` and evaluated at the photos step.
A tag will be assigned by evaluating a field in the record to be indexed with a regular expression. Like:

```
<tagger user="*" tag="medialab" field="album" expr="2012. zwerfpad|2015. brommeren" />
```


See the `import.xml` for how to do this. Once you run the photos step again, the tags are assigned and you can simply search for "tag:medialab", or specify a subset with this query.

The syntax of regular expression can be found on the internet. But a **very** brief explanation is:

- . is a placeholder for any character, \\. matches the dot. 
- | is an OR operator. 
  In the example we match 2 albums: "2012.zwerfpad" and "2015. brommeren" 
- .\* means zero or more characters
- .+ means 1 or more characters



### GPS coordinates

GPS coordinates are extracted from the photo and indexed if available.
If coordinates are available, you can find your photo's based in those coordinates by searching for instance `loc:zwolle`. This will show all photo's that are taken in or around Zwolle. This works by first translating Zwolle into coordinates (via a service running at the Bitmanager site) and then search around those coordinates.

If no coordinates are available, it is possible to match the time of the photo's to available gpx-tracks. This is outside the scope of this document and still experimental. Contact me if you want to know more.

Note that Whatsapp strips all metadata from photo's. Only the date is available, and in case of an IPhone, this date is in the creation date, which is not copied if you copy the file.

##### Google maps and Google places 

The Google services are free if you do only a limited requests per month (order of magnitude: 10000 requests per month). For personal use, this is more than enough. Therefore, if you want to make use of Google services, you need to get api-keys and put them in the `settings.xml` of your album website.

Google maps will be used if you want to view the photo on the map (if coordinates are available). You can reach the map via the context menu of a photo with coordinates. This option is only shown if you enabled the map! Enabling is done by modifying the settings.xml:

- Mark the maps node being active

- Creating a Google api-key in the Google console via [](https://console.cloud.google.com/google/maps-apis/overview) and fill that key in the `map/google` element.

If your website is accessible over the internet, you need a browser-key that allows access from your public domain (as referer).
If the album website is for internal use only, you need a server-key that enables access from your public IP. You can get your public IP via [](https://www.whatsmyip.org/) .

Google places will be used if the location service of the Bitmanager cannot resolve the location. The Bitmanager service is based on open source data from `geonames.org`. You can enable this service by requesting a server-key in the Google console for the Google Places API. The server key needs to give access to the Bitmanager server at IP: `45.142.234.255`. This key should be filled in the `mainindex/search/searchers/searcher` element. See the `settings.xml` for details.

***Warning***: you might want to set quota's and price limits. For small sites you probably never reach the limit where you are getting billed, but you never know!

In case you need a server-key for both maps as places, you can use the same key, but with 2 allowed IP's.



### Full vs Incremental import

The import engine has a lot of flags. Most of the time you can leave them as is. In that case an import step will be incremental. This means: preserve the existing content in the index, but add what has been changed.

If you want to start over, you can check the `Fullimport` checkbox. What happens next is that a complete new index is created, without using the existing data. 

If you do that for the captions/ocr/face-extraction, it means that this process starts over and will take a lot of time to complete (days). So use this flag with caution!



### Specific settings per directory (importsettings.xml)

By putting an importsettings.xml in a directory, you can customize how the directory and the subdirectories are indexed. An example is:

```
<root propagate="this" hide="external">
   <album src="FromDirectoryName"  />
   <date src="None"/>
   <camera src="fromMetadata,fromValue" value="scanner"/>
</root>
```

This example indicates that the photo's are only visible from the local network, not from the internet.  The name of the album is taken from the directory name, a date is not assigned and the camera is from the metadata, but if not found, the value "scanner" is used as camera.

Possible values for propagate are: `this, default, parent`. It specified what settings are propagated to a subdirectory. If you use "parent", this importsettings acts like a one-shot-setting. the settings are restored from the parent for subdirectories. The default is "this".

Another example:

```
<root inherit="fromDefault">
   <date src="assumeLocal,FromMetadata,FromFileDate" />
   <whatsapp src="FromNoThumbnail" />
</root>
```

In this example photo's without a thumbnail are considered to be imported from Whatsapp. By default the album name will be overwritten by "whatsapp". The date is fetched from the metadata, and if not found from the file date. 

Note that the **order is important**. "FromFileDate,FromMetadata" would try to take the date from the file first, which is always present and so the FromMetadata is useless then.

If the metadata did contain a date, but it had not timezone in it, the local timezone is assumed. Specifying "assumeUtc" would have assumed Universal Time for missing timezones.

The settings are not inherited from the parent directory, but from the defaults, because of `inherit="fromDefault"`.

As a last example:

```
<root >
   <album src="FromValue" value="Holiday Finland"  />
</root>
```

This example uses "Holiday Finland" as value for the album for all photo's in this directory and below.
