#
# Copyright © 2023, De Bitmanager
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#    http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.
#

from quart import Quart, request
import traceback
import logging
import asyncio
import sys,os,signal,inspect

from transformers import AutoProcessor, AutoModelForCausalLM
import requests
from PIL import Image
from googletrans import Translator

processor = AutoProcessor.from_pretrained("microsoft/git-base-coco")
model = AutoModelForCausalLM.from_pretrained("microsoft/git-base-coco")
translator = Translator()

print("loaded model.")


app = Quart(__name__)

@app.route('/ping')
async def pingHandler():
   return {"ping": "ok"}

@app.route('/shutdown')
async def quitHandler():
   print ("Received quit")
   os.kill(signal.CTRL_C_EVENT, 0)
   return {"shutdown": "ok"}

@app.route('/caption')
async def captionHandler():
   try:
      maxlen = request.args.get("maxlen")
      maxlen = 50 if maxlen is None else int(maxlen)
      fn = request.args.get("file")
      image = Image.open(fn)
      pixel_values = processor(images=image, return_tensors="pt").pixel_values
      generated_ids = model.generate(pixel_values=pixel_values, max_length=maxlen)
      captions = processor.batch_decode(generated_ids, skip_special_tokens=True)
      print(captions)
      caption_nl = []
      for x in captions:
         trResult = translator.translate(captions[0], src='en', dest='nl')
         if inspect.isawaitable(trResult):
            trResult = await trResult  # await if awaitable
         caption_nl.append(trResult.text)
      return {"captions_en": captions, "captions_nl": caption_nl}
   except Exception as e:
      ret = {"msg": str(e), "trace": traceback.format_exc().replace("\"", "'").splitlines()}
      return {"error": ret}, 500

@app.after_serving
async def afterServing():
   print ("Terminating application")
   os._exit(0)


print ("Starting http-server...")
app.run(port=5000)