---
name: summarize-document
description: Use when the user asks for a summary, overview or "the key points" of an uploaded file or topic.
---
Follow these steps to write a summary:

1. Call `search_documents` with the file name or topic the user mentioned. If the first search
   misses important parts, search again with 1-2 different queries (for example "conditions",
   "exceptions", "deadlines").
2. Use ONLY the passages that come back. Never add facts from your own knowledge.
3. Write the summary in this shape:
   - **In one sentence:** what the document is about.
   - **Key points:** 3 to 7 short bullets, the most important first.
   - **Watch out for:** deadlines, limits, exceptions or costs, if there are any.
4. End with the sources you used, like `[return-policy.md#0]`.
5. If nothing relevant was found, say so and list what the user could upload or ask instead.
