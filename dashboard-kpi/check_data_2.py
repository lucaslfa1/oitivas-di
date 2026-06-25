import pandas as pd
import os

try:
    path = 'editados/Base_BI_Sinistros_Escalas_v3.xlsx'
    df = pd.read_excel(path, sheet_name='Fato_Sinistro', nrows=10)
    print(df[['dtIncl', 'dtIni', 'dtFim']].head(10))
except Exception as e:
    print(f"Erro: {e}")
