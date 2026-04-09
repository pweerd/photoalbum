class Autocomplete {
   #argExpr;
   #lcArg;
   #levensteinArr;
   #transpose;

   constructor(arg, transpose) {
      if (arg instanceof RegExp) {
         this.#argExpr = arg;
         this.#lcArg = arg.toString().toLowerCase();
      } else {
         this.#lcArg = (arg + "").toLowerCase();
         this.#argExpr = new RegExp(this.#lcArg, "i");
      }
      this.#transpose = transpose ? true : false;
   }

   score(other) {
      const s = (other + "").toLowerCase();
      if (s.length === 0) return 0;
      const maxLen = Math.max(s.length, this.#lcArg.length);
      const m = this.#argExpr.exec(s);
      if (m !== null) {
         if (m.index === 0) return 2 + (m[0].length / s.length);
         else return 1 + (m[0].length / s.length);
      }
      return (maxLen - this.levenstein(s)) / maxLen;
   }

   levenstein(other) {
      const s1 = this.#lcArg;
      const s2 = other;
      const len1 = s1.length;
      const len2 = s2.length;
      if (len1 === 0 || len2 === 0) {
         return (len1 === len2) ? 0 : len1 + len2;
      }

      let arr0 = new Array(len2 + 1);
      let arr1 = new Array(len2 + 1);

      let i, j, tmparr, ch1, ch2, cost;
      arr0.fill(0);
      for (i = 0; i <= len2; i++) arr1[i] = i;

      if (this.#transpose) {
         let arr2 = new Array(len2 + 1);
         arr2.fill(0);
         for (i = 0; i < len1; i++) {
            arr0[0] = i;
            ch1 = s1.charCodeAt(i);
            for (j = 0; j < len2; j++) {
               ch2 = s2.charCodeAt(j);
               cost = (ch1 == ch2) ? 0 : 1;
               arr0[j + 1] = Math.min(1 + Math.min(arr0[j], arr1[j + 1]), arr1[j] + cost);
               if (i > 0 && j > 0 && ch1 === s2.charCodeAt(j - 1) && s1.charCodeAt(i-1) === ch2)
                  arr0[j + 1] = Math.min(arr0[j + 1], arr2[j - 1] + cost);
            }
            tmparr = arr2;
            arr2 = arr1;
            arr1 = arr0;
            arr0 = tmparr;
         }
         return arr1[len2];
      }


      //Non transpose version
      for (i = 0; i < len1; i++) {
         arr0[0] = i;
         ch1 = s1.charCodeAt(i);
         for (j = 0; j < len2; j++) {
            ch2 = s2.charCodeAt(j);
            cost = (ch1 == ch2) ? 0 : 1;
            if (j === len2 - 1)
               j = j + 0;
            arr0[j + 1] = Math.min(1 + Math.min(arr0[j], arr1[j + 1]), arr1[j] + cost);
         }
         tmparr = arr1;
         arr1 = arr0;
         arr0 = tmparr;
      }
      return arr1[len2];
   }


}

//let ac = new Autocomplete("aap", true);

//function dumpScore(x) {
//   console.log("ac (aap, ", x, ")==>", ac.score(x));
//}
////dumpScore("aapje");
////dumpScore("xaap");
////dumpScore("xap");
////dumpScore("axapje");

//ac = new Autocomplete("pweerd", false);
//dumpScore("Oma Jantje van der Weerd");
//dumpScore("Peter van der Weerd");
