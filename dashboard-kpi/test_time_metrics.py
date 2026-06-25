import pandas as pd
import os

try:
    if os.path.exists('editados/Base_BI_Sinistros_Escalas_v3.xlsx'):
        path = 'editados/Base_BI_Sinistros_Escalas_v3.xlsx'
    else:
        path = os.path.join(os.getcwd(), 'editados', 'Base_BI_Sinistros_Escalas_v3.xlsx')

    df = pd.read_excel(path, sheet_name='Fato_Sinistro')
    
    # Converter para datetime
    df['dtIni'] = pd.to_datetime(df['dtIni'], errors='coerce')
    df['dtFim'] = pd.to_datetime(df['dtFim'], errors='coerce')
    
    # Calcular duração em minutos
    df['Duracao_Minutos'] = (df['dtFim'] - df['dtIni']).dt.total_seconds() / 60
    
    # Filtrar durações negativas ou muito curtas (ruído) e muito longas (outliers > 48h)
    df_valid = df[(df['Duracao_Minutos'] > 0) & (df['Duracao_Minutos'] < 2880)].copy()
    
    print(f"Total de registros: {len(df)}")
    print(f"Registros válidos para cálculo de tempo: {len(df_valid)}")
    
    # Média Geral
    media_geral = df_valid['Duracao_Minutos'].mean()
    print(f"\nTempo Médio Geral: {media_geral:.2f} minutos ({media_geral/60:.2f} horas)")
    
    # Média por Escala (se disponível, carregando a dimensão para fazer o merge seria ideal, mas vamos tentar direto se tiver a coluna ou usar a lógica do app)
    # Vamos carregar a dimensão operador para ter a escala correta, igual ao app
    dim_operador_path = 'Dim_Operador_Inferida.csv'
    if not os.path.exists(dim_operador_path):
        dim_operador_path = os.path.join(os.getcwd(), 'Dim_Operador_Inferida.csv')
        
    if os.path.exists(dim_operador_path):
        dim_operador = pd.read_csv(dim_operador_path)
        df_valid = df_valid.merge(
            dim_operador[['Operador', 'Escala_Predominante']], 
            left_on='Operador_Inclusao', 
            right_on='Operador', 
            how='left'
        )
        df_valid['Escala'] = df_valid['Escala_Predominante'].fillna('Desconhecida')
        
        print("\n--- Tempo Médio por Escala (Minutos) ---")
        print(df_valid.groupby('Escala')['Duracao_Minutos'].mean().sort_values(ascending=False))
    
    print("\n--- Top 5 Operadores mais rápidos (Média em Minutos, min 5 envios) ---")
    media_op = df_valid.groupby('Operador_Inclusao')['Duracao_Minutos'].agg(['mean', 'count'])
    print(media_op[media_op['count'] >= 5].sort_values('mean').head(5))

    print("\n--- Top 5 Operadores mais lentos (Média em Minutos, min 5 envios) ---")
    print(media_op[media_op['count'] >= 5].sort_values('mean', ascending=False).head(5))

except Exception as e:
    print(f"Erro: {e}")
