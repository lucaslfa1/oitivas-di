import pandas as pd
import os

file_path = 'editados/Base_BI_Sinistros_Escalas_v3.xlsx'
if os.path.exists(file_path):
    df = pd.read_excel(file_path, sheet_name='Fato_Sinistro')
    print("Columns in Fato_Sinistro:")
    print(df.columns.tolist())
else:
    print(f"File not found: {file_path}")
