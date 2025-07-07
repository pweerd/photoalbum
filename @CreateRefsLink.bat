IF "%1" == "" echo Please supply the source for the _references link
rd _references
mklink /j _references %1