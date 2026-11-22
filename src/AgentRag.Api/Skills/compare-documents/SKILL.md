---
name: compare-documents
description: Use when the user asks to compare two or more files, products, policies or options, or asks "what is the difference between".
---
Follow these steps to compare:

1. Call `search_documents` once for EACH thing being compared (for example "standard shipping",
   then "express shipping"), so each side gets its own passages.
2. Pick the 3 to 6 aspects that matter most for the user's question (price, time, conditions, ...).
3. Answer with a Markdown table: one row per aspect, one column per thing being compared.
   Write "not mentioned" when the documents say nothing about an aspect - never guess.
4. Below the table, add one or two sentences with the most important difference.
5. End with the sources you used, like `[shipping.md#1]`.
