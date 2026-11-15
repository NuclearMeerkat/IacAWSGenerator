# Models

`ModelRouter` implements `IModelRouter`. With no endpoint it calls OpenRouter at `https://openrouter.ai/api/v1` using `OPENROUTER_API_KEY`.

`--endpoint` switches that run to a local server. Port `11434`, or a host containing `ollama`, uses OllamaSharp. Any other URL uses the OpenAI-compatible client. Model ids come from configuration, never from this project.

`PriceTable` reads `pricing` from configuration: USD per one million input and output tokens, keyed by model id. `ModelRouter.EstimateCostUsd` returns null when the id is missing. Add a price by editing `iacagent.json`, not by changing this project.
