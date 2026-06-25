import pandas as pd
import os

file_path = 'editados/Base_BI_Sinistros_Escalas_v3.xlsx'
df = pd.read_excel(file_path, sheet_name='Fato_Sinistro')
print(f"Total rows: {len(df)}")
print(f"Unique cdviag: {df['cdviag'].nunique()}")
print(f"Unique cdMonit: {df['cdMonit'].nunique()}")
print("Sample cdviag:", df['cdviag'].head(3).tolist())
print("Sample cdMonit:", df['cdMonit'].head(3).tolist())
