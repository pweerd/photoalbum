---
title: Help | Gezichten (album)
---
## Doel

Deze pagina is alleen toegankelijk vanaf de lokale PC of het lokale netwerk. Nooit vanaf het internet. Ook niet als de site toegankelijk is via het internet.

De pagina toont alle gezichten die uit de foto's geëxtraheerd zijn. Deze gezichten kunt u van een naam voorzien, of een automatisch toegekende naam corrigeren.

Het idee is dat u van elke persoon die u kent en die in de foto-collectie voorkomt +/- 10 verschillende gezichten van een naam voorziet.

Als u na afloop de stap "`face_matching`" gevolgd door de stap "`photos`" uit de importer draait, dan kunt u via deze pagina de automatische toekenningen controleren. In het album zelf zijn de namen standaard opzoekbaar zonder veld, maar als je specifiek een naam wilt is het handig om te zoeken met `naam:"naam van de persoon"`



## Linker paneel

In het linker paneel vindt u een lijst met namen met daarboven een kleine zoekbox.

De namen zijn afkomstig uit het bestand `facenames.txt`. 
WAARSCHUWING: U mag namen aanpassen en nieuwe namen aan het einde toevoegen. De volgorde van namen aanpassen mag ***absoluut niet***, omdat naams-toekenning wordt gedaan op basis van de index van deze naam binnen het bestand!
Als u het bestand aanpast zal dit meteen actief worden binnen de website. Het is handig om dit bestand na de eerste import aan te maken en te vullen met namen van mensen die u kent en in de collectie voorkomen.

 De zoekbox in het linker paneel ondersteunt zogenaamde reguliere expressies, waarmee de potentieel lange namenlijst even verkleind kan worden. Tikt u bijvoorbeeld `weerd` in deze zoekbox, dan worden alleen de namen waarin `weerd` voorkomt getoond. U kunt combineren met een '|'. Bijvoorbeeld `we|alp` waarin `we` of `alp` voorkomt. Voor meer informatie over reguliere expressies kunt u op het internet zoeken naar "syntax regex".

Door een naam aan te klikken verandert de cursor in een stempel en kan de naam gestempeld worden op een gezicht. Nog een keer klikken zet het stempel uit. Je hoeft dus niet te slepen. Eén keer een naam selecteren en dan kun je meerdere keren stempelen.
Als je op een naam klikt met de ctrl toets vastgehouden dan worden alle gezichten voor deze naam getoond.

Behalve de namen uit `facenames.txt` staan er ook "UNKNOWN" en "CLEAR" in. UNKNOWN gebruik je om aan te duiden dat dit een onbekende is, CLEAR gebruik je om de toekenning aan een gezicht te wissen.



## Acties bij het stempelen

Bij het paneel met de gezichten zijn de volgende acties mogelijk: 

- click op een gezicht
  Als een stempel actief is, dan wordt de naam aan dit gezicht toegekend. Zonder actief stempel wordt de foto waaruit dit gezicht geëxtraheerd werd in een nieuwe tab geopend.

- ctrl-click op een gezicht
  De foto waaruit dit gezicht geëxtraheerd werd wordt in een nieuwe tab geopend.

- alt-click op een gezicht
  Als een stempel actief is, dan wordt de naam aan dit gezicht toegekend in correctieve modus.
  Dwz dat de naam is toegekend, maar die gezicht zal niet als voorbeeld dienen om automatisch andere gezichten van naam te voorzien (tijdens de `face_match` procedure). 
  Zonder actief stempel doet deze actie niets.

Bij een toekenning wordt altijd bijgehouden wat de bron van deze toekenning was. Dit wordt middels een letter in het src-veld bijgehouden.  Mogelijkheden zijn:

- known vs unknown (resp: K of U)
- automatisch (A)
  Dit zijn toekenningen die in de face_match stap worden gedaan.
- manual (M)
  Dit zijn toekenningen die u vanuit dit scherm met de hand doet (een click met een stempel)
- correctie (C)
  Dit zijn toekenningen die u vanuit dit scherm doet via een alt_click met een stempel

Deze letters zijn te combineren tijdens het zoeken in het src-veld. Bv: AK=automatische toegekende gezichten uit de facenames.txt.



## Zoeken

In de zoekbox kun je zoeken met AND, OR, NOT (allemaal met hoofdletters). Je kunt met veld zoeken of zonder. Ook kun je haakje gebruiken om termen te groeperen. Als je zonder veld zoekt dan zoek je automatisch in de bestandsnaam en namen.
Let op: niet alle gegevens uit de hoofd-index zijn voorhanden. Zo zijn album en datum nog niet geëxtraheerd. Maar omdat het album meestal deel uitmaakt van de volledige bestandsnaam (dus inclusief directories) is deze wel opzoekbaar.

Voorbeelden van queries met velden (let op de ':' tussen veld en waarde):

- **src**:C
  zoekt alle gezichten die  gecorrigeerd zijn.
- **src**:AK
  zoekt alle bekende gezichten die automatisch zijn toegekend
- **gezichten**:2
  Zoekt gezichten die uit foto's met 2 personen komen.
- **naam**:peter of **naam**:"peter van der weerd"
  Zoekt gezichten waaraan de naam peter of "peter van der weerd" (laatste query) is toegekend
- **nameid**:10
  zoekt alle gezichten die toegekend zijn aan de 10e naam 
  Deze query wordt gebruikt als je ctrl-click op een naam uit het linker paneel doet...









