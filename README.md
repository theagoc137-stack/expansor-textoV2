# Expansor de Texto — versão 1.1.0 (revisão técnica 1)

Aplicativo Windows 10/11 que substitui atalhos digitados (ex.: `;ola`) por textos predefinidos em outros programas. Mantém o mecanismo validado na prova de conceito: `WH_KEYBOARD_LL` para monitorar o teclado e `SendInput` com entrada Unicode, **sem usar a área de transferência**, com intervalo padrão de **10 ms**.

**Criador exibido no aplicativo:** Theago Oliveira.

> **Status:** esta versão **ainda não foi compilada nem testada em Windows**. A compilação deve ser feita pelo GitHub Actions e os testes abaixo devem ser executados pelo usuário antes de considerá-la validada.

## Recursos

- C#/.NET 8 Windows Forms, sem Electron e sem pacotes externos. Sem internet, telemetria ou coleta de dados.
- Cadastro, edição, exclusão e pesquisa de atalhos; texto multilinha, acentos, símbolos e emojis.
- Configurações em JSON local. Fechar a janela mantém o app na bandeja.
- Pausar/ativar pela interface, pelo menu da bandeja ou por `Ctrl + Alt + F10`.
- Delimitadores configuráveis: Espaço, Enter, Tab e pontuação (campo próprio).
- Atraso configurável de 0 a 100 ms (padrão 10 ms).
- Inicialização opcional com o Windows (inicia minimizado na bandeja).
- Mascote vetorial no rodapé que acompanha o cursor enquanto a janela está visível; não intercepta cliques e para ao ocultar a janela.
- Executa sempre como usuário comum (`asInvoker`); nunca pede administrador.

## Delimitadores padrão

Espaço: ativado • Pontuação: `.,!?:;` • Enter: desativado • Tab: desativado.
Enter e Tab começam desativados porque podem enviar mensagem ou mudar o foco. Um delimitador desativado passa normalmente ao aplicativo e encerra a palavra (não contamina o próximo atalho).

## Como usar

1. Execute `ExpansorTexto.exe`.
2. Clique em **Novo**, informe o atalho (ex.: `;ola`) e o texto.
3. Clique em **Salvar** (ou `Ctrl + S`).
4. Digite o atalho em um campo de texto e finalize com um delimitador habilitado. O atalho é apagado, o texto inserido e o delimitador preservado.

Regras do atalho: sem espaços/Enter/Tab, sem emojis, até 64 caracteres, sem diferenciar maiúsculas de minúsculas. Um atalho que **termina** com um caractere de pontuação delimitador nunca será acionado (o app avisa ao salvar).

## Dados e logs

- Configuração: `%LOCALAPPDATA%\ExpansorTexto\config.json` (cópia anterior em `config.json.bak`).
- Log: `%LOCALAPPDATA%\ExpansorTexto\expansor.log` (rotaciona a 512 KB).
- O log **não** registra texto digitado, atalhos, textos de substituição nem títulos de janela: apenas eventos, contagens, códigos de erro e o nome do processo de destino.

## Compilar pelo GitHub Actions

1. Envie **o conteúdo desta pasta** para a raiz do repositório (o arquivo deve ficar exatamente em `.github/workflows/build-windows.yml`).
2. Abra **Actions → Build Expansor de Texto → Run workflow** (também roda a cada push em `main`/`master` que altere `.cs`, `.csproj`, `app.manifest` ou o workflow).
3. Ao terminar com sucesso, abra a execução e baixe o artefato **`expansor-texto-windows-x64`**.
4. Extraia o ZIP: contém `ExpansorTexto.exe` (single-file, self-contained, win-x64) e `ExpansorTexto.exe.sha256`. Não requer .NET instalado.

Se a execução falhar, copie a mensagem de erro do passo "Compilar e publicar" para a segunda revisão.

## Compatibilidade e limitações

- O funcionamento em Citrix/Salesforce **depende do ambiente e dos testes realizados**. O usuário relatou que 10 ms corrigiu o texto incorreto no Bloco de Notas, no chat do Salesforce dentro do Citrix e no Word; isso não garante outros ambientes. Se necessário, ajuste o atraso e compare.
- O monitor usa o layout de teclado **local** da janela ativa. Em sessões remotas com layout diferente do local, os caracteres vistos pelo app podem não coincidir com os digitados.
- Durante a expansão as teclas físicas são retidas por instantes e reenviadas depois (até 64 caracteres, só em memória, nunca gravadas). Evite digitar durante textos longos.
- Quebras de linha do texto são inseridas com a tecla Enter; em chats isso pode enviar a mensagem.
- Cliques do mouse não zeram o buffer (apenas troca de janela/campo, Backspace, setas e 10 s sem digitar).
- Alguns aplicativos, campos protegidos, janelas elevadas (administrador), telas seguras ou políticas corporativas bloqueiam entrada simulada. Não execute como administrador para contornar; consulte a TI.
- O executável não é assinado digitalmente; Windows ou antivírus corporativo podem alertar.

## Testes recomendados após compilar

1. Bloco de Notas: `;teste` + espaço.
2. Salesforce Chat no Citrix, outra aba/campo do Citrix e Word: texto completo e delimitador preservado.
3. Pontuação: `;teste,` e `;teste.`.
4. Delimitador desativado: com Espaço desligado, digitar `;teste ` e depois `;teste.` — nada deve ser expandido indevidamente.
5. Enter/Tab: ativar só em campo de teste.
6. Texto multilinha, acentos (`ç`, `á`, `ã`), emoji e atalho com acento (`;olá`, tecla morta ´ + a).
7. Pausar/reativar pelo botão, bandeja e `Ctrl + Alt + F10`; fechar para a bandeja e reabrir.
8. Editar um atalho e digitar na pesquisa: o texto do editor não pode ser apagado.
9. Reiniciar o app e confirmar que atalhos e opções persistiram; testar "Iniciar com Windows".
10. Janela oculta: o mascote não deve consumir CPU (Gerenciador de Tarefas).
