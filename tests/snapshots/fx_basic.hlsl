float4 main(float4 p : POSITION) : SV_Position { return p; }

technique Simple {
    pass First {
        VertexShader = compile vs_2_0 main();
        PixelShader = compile ps_2_0 main();
        ZEnable = TRUE;
    }
}
