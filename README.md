# Mistral AI Console App

## Setup
1. Get a free API key at https://mistral.ai
2. In your terminal run: `export MISTRAL_API_KEY="your-key-here"`
3. Connect to Umbraco Database, (Done with VSCode SQL Extension)

## Run
```
dotnet build

dotnet run
```

## Notes
- Model choice: mistral-small is faster, mistral-large is more capable
- The API key is never hardcoded — it is read from your environment variables