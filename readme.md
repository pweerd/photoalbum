# Photoalbum website

## General

The album site consist of:

- a lightbox with your photo's
- a detail view of a photo, slideshow, etc
- a faces view where you can see the extracted faces and stamp them with a name.
- a map view where you can find the locations (if photo's are supporting locations of course)
- an importer that extract and combines information to be included in the index.

It runs on premise from an Elasticsearch index. Currently the importer supports OCR-ring of the photos, caption generation and face recognition. All this information is searchable.



## Install & getting started

An installer is available. Download it here: [](https://bitmanager.nl/distrib/).
The installer will install:

- the photo website + importer
- Elasticsearch V9
- Python + modules + AI model (needed for caption generation)
- ffmpeg
- exiftool
- Bitmanager core components (needed for logviewer)

The installer creates some links, including a getting-started document that explains the best way to start importing your photo's.
This getting started document is also found here: [](https://bitmanager.nl/albumdocs/getting_started.htm).
A document describing some possibilities in the website: [](https://bitmanager.nl/albumdocs/help_nl.htm) 



#### Stemmer (language-support)

Currently the only supported language is Dutch. The English captions are translated into Dutch and the Elasticsearch photo index has a Dutch stemmer. 

Unfortunately Elastic ditched the better KP-stemmer and now only supports the Ducth Porter stemmer, which stems poorly. Therefore the installer ships a small plugin that revives the Dutch KP-stemmer. Using this plugin with a different version of Elastic will not work. If its still V9, you might have success by modifying the version in the `plugin-descriptor.properties` file.

Future versions of Elastic will support the Dutch KP-stemmer again, but then as standard 'dutch'.

Of course you can modify the `index.config.js` file and put whatever stemmer in there (for instance a dictionary based hunspell stemmer)



#### Compilation and needed references

The project is created with VS2022. It is probably not possible to compile the project with earlier versions of VS.

For the needed references it is best to install the application first and 

Make sure you have a sub-folder '_references' in your solutions folder. This can be a junction to a common folder (see `@CreateRefsLink`) or a real folder.
All needed references can be found from **the zip that is supplied in the release**. But you can also copy them from the directory where the installer copied them.



