---
title: Help | Album
---
## Algemeen

Het fotoalbum toont de geïndexeerde foto's en video's (eventueel een subset van...). Deze items kunnen opgedeeld zijn in meerdere albums.

De importer heeft de items

- Van een album-naam voorzien en de datum, camera, GPS-coördinaten geïndexeerd

- Ge-OCR-ed
Zodat eventuele tekst doorzoekbaar is. Dit gaat helaas de ene keer beter dan de andere.

- Automatisch van een doorzoekbaar bijschrift voorzien
Zo'n bijschrift bevat dingen als "een familie op het strand", "een meisje met een hond", etc.

- Gezichten geëxtraheerd en daar namen aan toegekend.

Al deze zaken zijn doorzoekbaar.

Het album wordt standaard gebruikt om foto's te groeperen. Maar tijdens het zoeken wordt deze strategie verlaten en worden gewoon alle foto's die aan de zoekopdracht voldoen getoond. Na een zoekopdracht kan wel weer gefilterd worden op jaar en/of album.




## Informatie

De omcirkelde "i" op een foto laat informatie over de betreffende foto zien. In een mobiele omgeving moet je hier op klikken, in een desktop omgeving kun je gewoon je muis erboven laten hangen.



## Zoeken

In de zoekbox kun je zoeken met AND, OR, NOT (allemaal met hoofdletters). Je kunt met veld zoeken of zonder. Ook kun je haakje gebruiken om termen te groeperen. Als je zonder veld (of met veld \*)  zoekt, dan zoek je automatisch in de bestandsnaam, album, bijschrift , ocr-text en namen.

Voorbeelden van zoeken met velden (let op de ':' tussen veld en waarde):

- **type**:video
  zoekt alle video's.
- **plaats**:boxtel (of loc, bv **loc**:belgie)
- **seizoen**:herfst of **seizoen**:herfst~ (herfst +- 1 maand)
- **datum**:2020-06-13 (of gedeeltelijk, bv: **datum**:2020-06)
- **ocr**:toegang
- **gezichten**:2
  Zoekt foto's waar 2 mensen op staan.
- **naam**:peter of **naam**:"peter van der weerd" of **naam**:(peter wilma)
  Zoekt foto's waar Peter op staat, of waar Peter en Wilma samen op staan (laatste query)
- **camera**:canon

Ook is er een simpele taxonomie gebruikt, waardoor je nu kunt zoeken of "dier" of "mens" of "vogel", etc. Voorbeeld: een meeuw zal ook gevonden worden als 'vogel' of als 'dier'.

#### Wijziging in veld-interpretatie.

Bij wijze van experiment worden alle woorden die achter een veld staan bij elkaar geveegd als horende bij dat veld.

- nu: `naam:peter wilma` zal geïnterpreteerd worden als `naam:(peter AND wilma)`
- vroeger: `naam:peter wilma` werd geïnterpreteerd als `naam:peter AND wilma`
  hierbij werd wilma dus gezocht in de default velden (vrijwel alles)

Dit lijkt meer intuitief, maar heeft natuurlijk ook een keerzijde. Het bij elkaar vegen wordt gestopt zodra er een ander veld, haakjes of operator komt. Voorbeeld van een ongewenste samenvoeging::

- `plaats:duitsland fietsen`
  Je bedoelt hier waarschijnlijk fietsen ergens in Duitsland. Dit kun je oplossen door eerst alle lossen woorden te tikken en pas daarna de velden. Ook kun je de woorden in het \*-veld stoppen (alle velden). Bijvoorbeeld:
  - `fietsen plaats:duitsland`
  - `plaats:duitsland *:fietsen`



## Contextmenu

Iedere foto heeft een contextmenu met bv opties om het hele album te tonen, naar de kaart te gaan, etc.

Het contextmenu is te bereiken met de rechtermuis (desktop) of long-click(mobiel)



## Kaart

Als de positie van een foto bekend is kan een kaart getoond worden.

Afhankelijk van het zoom-nivo van de kaart zullen foto's geclusterd worden.

Klikken op een pin laat de foto weer in de lightbox zien, met alle foto's in de buurt (gesorteerd op afstand van de geselecteerde foto)

Als de kaart geopend wordt met CTRL-click in het contextmenu, dan wordt de kaart in een nieuwe tab geopend.
