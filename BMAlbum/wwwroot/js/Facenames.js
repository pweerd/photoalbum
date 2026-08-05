class FaceNames {
   _chgid;
   _names;
   _sortedNames;

   constructor(sortedNames, chgid) {
      this._chgid = chgid === undefined ? -1 : chgid;
      this._sortedNames = sortedNames ?? [];
      const names = Array.from(this._sortedNames);
      names.sort((a, b) => a.id - b.id);
      this._names = names;
   }

   nameById (id) {
      return this._names[parseInt(id) + 2].name;
   };
   
   getFirstMatchingName (str, sorted)  {
      const names = sorted ? this._sortedNames : this._names;
      const re = new RegExp(str ?? ".", "i");
      const arglen = str ? str.length : 0;

      let firstPrefix = -1;
      let firstContains = -1;
      for (let i=0; i<names.length; i++) {
         const name = names[i];
         const m = re.exec(name.name);
         if (m===null) continue;
         if (m.length >= arglen) return name;
         if (m.offset === 0) {
            if (firstPrefix < 0) firstPrefix = i;
         } else {
            if (firstContains < 0) firstContains = i;
         }
      }
      if (firstPrefix >= 0) return names[firstPrefix] ;
      if (firstContains >= 0) return names[firstContains] ;
      return undefined;
   };


   getMatchingNames (str, sorted)  {
      const names = sorted ? this._sortedNames : this._names;
      const re = new RegExp(str ?? ".", "i");
      const arr = [];
      for (let i=0; i<names.length; i++) {
         const name = names[i];
         if (name.id >= 0 && !re.test(name.name)) continue;
         arr.push(name);
      }
      return arr;
   };
}
